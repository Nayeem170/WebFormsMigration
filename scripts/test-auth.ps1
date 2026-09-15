# Phase 7 edge-security verification. Runs while the stack is LOOPBACK-BOUND
# (the sequencing rule: the bind change is gated on THIS passing first).
#   - public reads stay public
#   - unauthenticated writes rejected (401 for API, 302 to IdP for browsers)
#   - a valid realm token authenticates writes
#   - an invalid token does not
#   - rate limits trip under burst and are keyed on the socket (not XFF)
#   - TLS terminates at the gateway
#   - the edge publishes exactly one realm (no master admin oracle)
#   - PKCE is enforced by the server, not by client behavior
#   - brute-force lockout is per-account (the axis the limiter can't cover)
param(
    [string]$GatewayUrl = 'http://127.0.0.1:8080',
    [string]$TlsUrl = 'https://127.0.0.1:8443',
    # Default is the ROUTED token endpoint (through the gateway's TLS
    # listener): the password grant doubles as proof that the routed IdP
    # path serves backchannel-style clients, not just redirects. The
    # minting client is the dedicated test-only client: the gateway's
    # own client has NO direct grants (browser code flow only).
    [string]$TokenEndpoint = '',
    [string]$ClientId = 'ccw-suite',
    [string]$Username = 'tester',
    [string]$Password = 'localdev-tester',
    # How to reach psql. Compose default; k8s runs pass the kubectl-wrapped
    # form: 'kubectl -n corewebforms exec deploy/postgres -- env
    # PGPASSWORD=localdev psql'
    [string]$Psql = 'docker exec -e PGPASSWORD=localdev corewebforms-postgres-1 psql'
)
if (-not $TokenEndpoint) { $TokenEndpoint = "$TlsUrl/realms/corewebforms/protocol/openid-connect/token" }
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

# --- public reads ----------------------------------------------------------
$page = Invoke-WebRequest "$GatewayUrl/" -UseBasicParsing -SkipHttpErrorCheck
Check 'public dashboard read stays public (200)' ($page.StatusCode -eq 200)

# --- unauthenticated writes rejected --------------------------------------
$apiPost = Invoke-WebRequest "$GatewayUrl/Default.aspx" -Method Post -UseBasicParsing -SkipHttpErrorCheck `
    -Headers @{ Accept = 'application/json' } -Body 'x=1'
Check 'unauthenticated API write rejected 401' ($apiPost.StatusCode -eq 401)
Check '401 advertises Bearer challenge' ("$($apiPost.Headers['WWW-Authenticate'])" -match 'Bearer')

# PS7 throws on -MaximumRedirection 0 rather than returning the 302, so
# pull the redirect response out of the exception.
$browserPost = $null
try {
    Invoke-WebRequest "$GatewayUrl/Default.aspx" -Method Post -UseBasicParsing `
        -Headers @{ Accept = 'text/html' } -Body 'x=1' -MaximumRedirection 0 | Out-Null
} catch { $browserPost = $_.Exception.Response }
Check 'unauthenticated browser write redirects to IdP (302)' ($null -ne $browserPost -and [int]$browserPost.StatusCode -eq 302)
Check 'IdP redirect points at the realm authorize endpoint' ("$($browserPost.Headers.Location)" -match '/realms/corewebforms/protocol/openid-connect/auth')

# --- valid token authenticates --------------------------------------------
$token = $null
try {
    $form = @{ grant_type = 'password'; client_id = $ClientId; username = $Username; password = $Password }
    $token = (Invoke-RestMethod -Method Post -Uri $TokenEndpoint -Body $form -SkipCertificateCheck).access_token
    } catch { Write-Host "  token acquisition failed: $_" }
    Check 'password grant via suite client yields an access token' ($null -ne $token -and $token.Length -gt 50)

if ($token) {
    $authed = Invoke-WebRequest "$GatewayUrl/Default.aspx" -Method Post -UseBasicParsing -SkipHttpErrorCheck `
        -Headers @{ Authorization = "Bearer $token"; Accept = 'application/json' } -Body 'x=1'
    Check 'authenticated write passes the gate (not 401/403)' ($authed.StatusCode -notin @(401, 403))

    $forged = Invoke-WebRequest "$GatewayUrl/Default.aspx" -Method Post -UseBasicParsing -SkipHttpErrorCheck `
        -Headers @{ Authorization = 'Bearer not-a-real-token'; Accept = 'application/json' } -Body 'x=1'
    Check 'invalid token still rejected 401' ($forged.StatusCode -eq 401)
}

# --- browser login end-to-end over the ROUTED IdP ---------------------------
# This is the check that gates the bind change: scripted, but it walks the
# exact browser path (302 to IdP -> login form -> form_post code -> callback
# -> session cookie), all against the gateway's TLS listener, so the routed
# Keycloak must answer with browser-facing URLs that a loopback browser can
# reach. Correlation/nonce cookies (SameSite=None; Secure) must survive the
# whole walk or the callback rejects the code.
$flowStep = 'redirect to IdP'
try {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $redirect = $null
    try {
        Invoke-WebRequest "$TlsUrl/Default.aspx" -Method Post -WebSession $session -SkipCertificateCheck `
            -Headers @{ Accept = 'text/html' } -Body 'x=1' -MaximumRedirection 0 | Out-Null
    } catch { $redirect = $_.Exception.Response }
    $authorizeUrl = "$($redirect.Headers.Location)"
    # PS7 stores the 302's Set-Cookie into the WebSession natively (the
    # .NET nonce cookie is name=<nonce>, value=N - that is by design; the
    # correlation+nonce cookies must ride the whole walk or the callback
    # rejects the code).
    $loginPage = Invoke-WebRequest $authorizeUrl -WebSession $session -SkipCertificateCheck -UseBasicParsing
    $form = [regex]::Match($loginPage.Content, '(?i)<form[^>]*action="([^"]+)"[^>]*>')
    if (-not $form.Success) { throw 'no form on the login page' }
    $action = $form.Groups[1].Value -replace '&amp;', '&'
    $fields = @{}
    foreach ($m in [regex]::Matches($loginPage.Content, '(?i)<input[^>]*type="hidden"[^>]*>')) {
        $n = [regex]::Match($m.Value, '(?i)name="([^"]+)"').Groups[1].Value
        $v = [regex]::Match($m.Value, '(?i)value="([^"]*)"').Groups[1].Value
        if ($n) { $fields[$n] = $v }
    }
    $fields['username'] = $Username
    $fields['password'] = $Password

    $flowStep = 'credential post to login-actions'
    $authResult = Invoke-WebRequest $action -Method Post -Body $fields -WebSession $session -SkipCertificateCheck -UseBasicParsing
    $form = [regex]::Match($authResult.Content, '(?i)<form[^>]*action="([^"]+)"[^>]*>')
    if (-not $form.Success) { throw 'no form_post code page (action missing)' }
    $callback = $form.Groups[1].Value -replace '&amp;', '&'
    $codeFields = @{}
    foreach ($m in [regex]::Matches($authResult.Content, '(?i)<input[^>]*type="hidden"[^>]*>')) {
        $n = [regex]::Match($m.Value, '(?i)name="([^"]+)"').Groups[1].Value
        $v = [regex]::Match($m.Value, '(?i)value="([^"]*)"').Groups[1].Value
        if ($n) { $codeFields[$n] = $v }
    }
    if (-not $codeFields.ContainsKey('code')) { throw 'no authorization code in the form_post page' }

    $flowStep = 'form_post code to /signin-oidc'
    $final = $null
    try {
        Invoke-WebRequest $callback -Method Post -Body $codeFields -WebSession $session -SkipCertificateCheck `
            -MaximumRedirection 0 | Out-Null
    } catch { $final = $_.Exception.Response }
    $sessionCookie = @($session.Cookies.GetCookies($TlsUrl) | Where-Object { $_.Name -eq 'ccw.session' })
    Check 'browser login completes over the routed IdP (session cookie issued)' ($sessionCookie.Count -eq 1)
    if ($sessionCookie.Count -ne 1) { Write-Host "  browser flow failed at: $flowStep" }
} catch {
    Check 'browser login completes over the routed IdP (session cookie issued)' ($false)
    Write-Host "  browser flow failed at: $flowStep ($_)"
}

# --- edge publishes exactly one realm ---------------------------------------
# The route is /realms/corewebforms/... only: the master realm (admin-cli,
# bootstrap admin) must NOT have a token endpoint through the edge. Prove
# the negative by TRYING the oracle: POST admin credentials to the routed
# master token path and require that no grant is processed. A 200 with a
# token would mean the whole master realm leaked past the edge.
$masterResp = $null
$masterBody = ''
try {
    $r = Invoke-WebRequest "$TlsUrl/realms/master/protocol/openid-connect/token" -Method Post `
        -UseBasicParsing -SkipHttpErrorCheck -SkipCertificateCheck -MaximumRedirection 0 `
        -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = 'admin'; password = 'localdev' }
    $masterResp = $r; $masterBody = "$($r.Content)"
} catch { $masterResp = $_.Exception.Response }
$masterCode = if ($masterResp) { [int]$masterResp.StatusCode } else { 0 }
Check 'master realm has no token endpoint at the edge (no admin oracle)' `
    ($masterCode -ne 200 -and $masterBody -notmatch 'access_token')

# --- PKCE is enforced by the SERVER, not by client behavior -----------------
# The ASP.NET handler always sends a code_challenge, but Keycloak must
# REFUSE one that is missing: with a public client and no secret at the
# code exchange, PKCE is the only thing binding a redeemed code to the
# browser that started the flow. Keycloak 26 surfaces this refusal as an
# empty non-200, which alone proves little - so the check is an A/B: the
# IDENTICAL authorize request plus a valid S256 challenge must render
# the login form (200). Only the challenge differs; only the refusal
# distinguishes them.
$verifier = 'pkce-verifier-abcdefghijklmnopqrstuvwxyz0123456789ABCDEF'
$sha = [System.Security.Cryptography.SHA256]::Create()
$challenge = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($verifier))) `
    -replace '\+', '-' -replace '/', '_' -replace '=', ''
$authzBase = "$TlsUrl/realms/corewebforms/protocol/openid-connect/auth" +
    "?client_id=gateway&response_type=code&scope=openid&state=x&nonce=y" +
    "&redirect_uri=$([uri]::EscapeDataString("$TlsUrl/signin-oidc"))"
$noPkce = Invoke-WebRequest $authzBase -UseBasicParsing -SkipHttpErrorCheck -SkipCertificateCheck
$withPkce = Invoke-WebRequest "$authzBase&code_challenge=$challenge&code_challenge_method=S256" `
    -UseBasicParsing -SkipHttpErrorCheck -SkipCertificateCheck
Check 'authorize without PKCE challenge refused (server-enforced S256)' `
    ($noPkce.StatusCode -ne 200 -and "$($noPkce.Content)" -notmatch '(?i)<form' `
    -and $withPkce.StatusCode -eq 200 -and "$($withPkce.Content)" -match '(?i)<form')

# --- brute-force lockout is per-account -------------------------------------
# The rate limiter bounds one SOURCE ADDRESS; password guessing
# distributes across addresses, so the realm carries
# bruteForceProtected (failureFactor 3 locally). Drive three wrong
# grants for the dedicated probe user, then require the CORRECT password
# to be rejected too - lockout that spares the right password is not
# lockout.
$bfBad = @{ grant_type = 'password'; client_id = $ClientId; username = 'bf-probe'; password = 'not-the-password' }
1..3 | ForEach-Object {
    Invoke-WebRequest $TokenEndpoint -Method Post -Body $bfBad -UseBasicParsing `
        -SkipHttpErrorCheck -SkipCertificateCheck | Out-Null
}
$bfGood = Invoke-WebRequest $TokenEndpoint -Method Post -UseBasicParsing -SkipHttpErrorCheck -SkipCertificateCheck `
    -Body @{ grant_type = 'password'; client_id = $ClientId; username = 'bf-probe'; password = 'bf-probe-pw' }
Check 'brute-force lockout denies the CORRECT password (per-account)' `
    ($bfGood.StatusCode -ne 200 -or "$($bfGood.Content)" -notmatch 'access_token')

# --- rate limits: socket-keyed, trip under burst --------------------------
# Parallel: sequential IWR (~25 ms each) cannot exceed a 500/10s window.
$burst = 1..600 | ForEach-Object -Parallel {
    try { (Invoke-WebRequest "$using:GatewayUrl/favicon.ico" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10).StatusCode }
    catch { 0 }
} -ThrottleLimit 32
$throttled = @($burst | Where-Object { $_ -eq 429 }).Count
Check "per-IP rate limit trips under 600-burst ($throttled x 429)" ($throttled -ge 1)
# A forged X-Forwarded-For must not buy extra quota. Deterministic form:
# confirm the socket bucket is CURRENTLY throttled (a plain request 429s),
# then immediately send requests with rotating XFF values - they must still
# 429, because the limiter never looks at the header.
$hot = $false
foreach ($i in 1..3) {
    try { if ((Invoke-WebRequest "$GatewayUrl/favicon.ico" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10).StatusCode -eq 429) { $hot = $true; break } } catch {}
    # Re-heat with a full burst; a smaller one cannot exceed the window.
    1..600 | ForEach-Object -Parallel {
        try { (Invoke-WebRequest "$using:GatewayUrl/favicon.ico" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
    } -ThrottleLimit 32 | Out-Null
}
$xffCodes = 1..40 | ForEach-Object -Parallel {
    try {
        (Invoke-WebRequest "$using:GatewayUrl/favicon.ico" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10 `
            -Headers @{ 'X-Forwarded-For' = "10.9.9.$(Get-Random -Maximum 254)" }).StatusCode
    } catch { 0 }
} -ThrottleLimit 32
Check 'rotating X-Forwarded-For does not buy quota (limiter is socket-keyed)' ($hot -and @($xffCodes | Where-Object { $_ -eq 429 }).Count -ge 1)

# --- TLS termination -------------------------------------------------------
# Accept 429: this check runs right after the burst, and the limiter keyed
# on our own socket is still hot. The point is that TLS terminated and HTTP
# answered, which a 429 proves as well as a 200.
try {
    $tls = Invoke-WebRequest $TlsUrl -UseBasicParsing -SkipCertificateCheck -SkipHttpErrorCheck
    Check 'TLS terminates at the gateway (answers over https)' ($tls.StatusCode -in 200, 429)
} catch {
    Check 'TLS terminates at the gateway (answers over https)' ($false)
}

# --- role split: ccw_app is DML-only ---------------------------------------
# Negative check with a built-in positive control: the identical CREATE
# TABLE must fail as ccw_app (permission denied) and succeed as
# ccw_migrator. A "failure" that is anything but permission denied (bad
# password, missing role, wrong database) does not count as a pass.
function Invoke-Psql([string]$User, [string]$Sql) {
    $out = Invoke-Expression "$Psql -h 127.0.0.1 -U $User -d ccw_catalog -c '$Sql' 2>&1"
    [pscustomobject]@{ Output = "$out"; Code = $LASTEXITCODE }
}
$appCreate = Invoke-Psql ccw_app 'CREATE TABLE ccw_probe (id int)'
Check 'ccw_app cannot CREATE TABLE (permission denied)' `
    ($appCreate.Output -match 'permission denied' -and $appCreate.Code -ne 0)
$migCreate = Invoke-Psql ccw_migrator 'CREATE TABLE ccw_probe (id int)'
Check 'ccw_migrator can CREATE TABLE (positive control)' ($migCreate.Code -eq 0 -and $migCreate.Output -notmatch 'ERROR')
Invoke-Psql ccw_migrator 'DROP TABLE IF EXISTS ccw_probe' | Out-Null

if ($failures -gt 0) { throw "test-auth: $failures failing check(s)" }
Write-Host 'test-auth: all checks passed'

# Generates the local TLS cert for the gateway (dev-only, self-signed).
# Written to infra/certs/gateway.pfx; NOT committed (gitignored) - it is a
# local dev artifact like the localdev credentials.
param(
    [string]$OutDir = "$PSScriptRoot\..\infra\certs",
    [string]$Password = 'localdev-cert',
    # SANs for the future non-loopback bind: the real hostname(s) the cert
    # must answer for, passed at regeneration time (see the Phase 7 plan).
    [string[]]$ExtraDnsName = @()
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$out = Join-Path $OutDir 'gateway.pfx'
# SANs cover both loopback spellings plus any future real hostname. Browsers
# reject the cert before TLS ever runs if the SAN is missing, so this list
# and the bind change must move together.
$dnsNames = @('CCW Local Gateway', 'localhost', '127.0.0.1') + $ExtraDnsName
if ($IsWindows) {
    $cert = New-SelfSignedCertificate -DnsName $dnsNames `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsageProperty All -KeyUsage DigitalSignature, KeyEncipherment -NotAfter (Get-Date).AddYears(5)
    Export-PfxCertificate -Cert $cert -FilePath $out -Password (ConvertTo-SecureString $Password -AsPlainText -Force) | Out-Null
    Remove-Item ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -ErrorAction SilentlyContinue
}
else {
    # New-SelfSignedCertificate/Export-PfxCertificate are Windows-only (PKI
    # module); build the same cert through .NET on Linux (CI runners).
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=CCW Local Gateway', $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    foreach ($name in $dnsNames) {
        if ($name -match '^\d{1,3}(\.\d{1,3}){3}$') {
            $san.AddIPAddress([System.Net.IPAddress]::Parse($name))
        }
        else {
            $san.AddDnsName($name)
        }
    }
    $req.CertificateExtensions.Add($san.Build())
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
    $raw = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(5))
    $withKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($raw, $rsa)
    [System.IO.File]::WriteAllBytes($out, $withKey.Export(
        [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $Password))
    $rsa.Dispose()
}
(Get-Item $out).FullName

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
# reject the cert before OIDC ever runs if the SAN is missing, so this list
# and the bind change must move together.
$cert = New-SelfSignedCertificate -DnsName (@('CCW Local Gateway', 'localhost', '127.0.0.1') + $ExtraDnsName) `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsageProperty All -KeyUsage DigitalSignature, KeyEncipherment -NotAfter (Get-Date).AddYears(5)
Export-PfxCertificate -Cert $cert -FilePath $out -Password (ConvertTo-SecureString $Password -AsPlainText -Force) | Out-Null
Remove-Item ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -ErrorAction SilentlyContinue
(Get-Item $out).FullName

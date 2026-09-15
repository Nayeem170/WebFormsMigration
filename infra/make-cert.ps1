# Generates the local TLS cert for the gateway (dev-only, self-signed).
# Written to infra/certs/gateway.pfx; NOT committed (gitignored) - it is a
# local dev artifact like the localdev credentials.
param(
    [string]$OutDir = "$PSScriptRoot\..\infra\certs",
    [string]$Password = 'localdev-cert'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$out = Join-Path $OutDir 'gateway.pfx'
$cn = 'CCW Local Gateway'
$cert = New-SelfSignedCertificate -DnsName $cn -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsageProperty All -KeyUsage DigitalSignature, KeyEncipherment -NotAfter (Get-Date).AddYears(5)
Export-PfxCertificate -Cert $cert -FilePath $out -Password (ConvertTo-SecureString $Password -AsPlainText -Force) | Out-Null
Remove-Item ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -ErrorAction SilentlyContinue
(Get-Item $out).FullName

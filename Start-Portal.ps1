param(
    [string]$LocalAddress,
    [int]$HttpsPort = 8443,
    [int]$CertificatePort = 8081,
    [switch]$SkipBuild,
    [switch]$DatabaseTools
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    $dockerPath = @(
        "$env:LOCALAPPDATA/Programs/DockerDesktop/resources/bin/docker.exe",
        "$env:ProgramFiles/Docker/Docker/resources/bin/docker.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $dockerPath) { throw 'Install and start Docker Desktop first.' }
    Set-Alias -Name docker -Value $dockerPath
}
$compose = @('compose', '-f', 'compose.yaml')
if ($DatabaseTools) { $compose += @('-f', 'compose.database-tools.yaml') }
if (-not $LocalAddress) {
    $network = Get-NetIPConfiguration | Where-Object {
        $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up'
    } | Sort-Object { $_.NetIPv4Interface.InterfaceMetric } | Select-Object -First 1
    $LocalAddress = $network.IPv4Address.IPAddress | Select-Object -First 1
}
if (-not $LocalAddress) {
    throw 'Connect this computer to Wi-Fi or Ethernet before starting phone access.'
}
$address = [System.Net.IPAddress]::Parse($LocalAddress)
$bytes = $address.GetAddressBytes()
if ($bytes.Length -ne 4 -or -not ($bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168))) {
    throw 'Use the computer private Wi-Fi or Ethernet IPv4 address.'
}
if ($HttpsPort -lt 1024 -or $HttpsPort -gt 65535 -or $CertificatePort -lt 1024 -or $CertificatePort -gt 65535 -or $HttpsPort -eq $CertificatePort) {
    throw 'Choose different HTTPS and certificate ports between 1024 and 65535.'
}

$env:PORTAL_LAN_IP = $LocalAddress
$env:PORTAL_HTTPS_PORT = $HttpsPort.ToString()
$env:PORTAL_HTTPS_BIND = "${LocalAddress}:$HttpsPort"
$env:PORTAL_SETUP_BIND = "${LocalAddress}:$CertificatePort"
$env:PORTAL_PUBLIC_URL = "https://${LocalAddress}:$HttpsPort"
$arguments = $compose + @('up', '-d', '--wait', '--wait-timeout', '300')
if (-not $SkipBuild) { $arguments += '--build' }
& docker @arguments
if ($LASTEXITCODE -ne 0) { throw 'Docker startup did not complete. Check docker compose logs.' }
& docker @compose up --no-build --no-deps --force-recreate -d --wait --wait-timeout 180 api web
if ($LASTEXITCODE -ne 0) { throw 'HTTPS application restart did not complete.' }

$directory = Join-Path $PSScriptRoot '.local-run/https'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$certificatePath = Join-Path $directory 'authority.cer'
& docker @compose cp bootstrap:/settings/https/authority.cer $certificatePath
if ($LASTEXITCODE -ne 0) { throw 'Could not export the public local certificate.' }
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
try {
    $fingerprint = $certificate.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not (Test-Path -LiteralPath ("Cert:\CurrentUser\Root\" + $certificate.Thumbprint))) {
        & certutil.exe -user -addstore Root $certificatePath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath ("Cert:\CurrentUser\Root\" + $certificate.Thumbprint))) {
            throw 'The local certificate authority was not installed for this Windows user.'
        }
    }
} finally {
    $certificate.Dispose()
}

$firewall = Join-Path $PSScriptRoot 'infrastructure/Enable-PhoneAccess.ps1'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$rule = Get-NetFirewallRule -Name 'DisputePortal-AssessmentHttps' -ErrorAction SilentlyContinue
$needsFirewall = -not $rule
if ($rule) {
    $filter = $rule | Get-NetFirewallAddressFilter
    $ports = $rule | Get-NetFirewallPortFilter
    $application = $rule | Get-NetFirewallApplicationFilter
    $service = $rule | Get-NetFirewallServiceFilter
    $addressAllowed = $LocalAddress -in $filter.LocalAddress
    $remoteAllowed = 'LocalSubnet' -in $filter.RemoteAddress
    $portsAllowed = $HttpsPort.ToString() -in $ports.LocalPort -and $CertificatePort.ToString() -in $ports.LocalPort
    $needsFirewall = -not ($addressAllowed -and $remoteAllowed -and $portsAllowed -and $rule.Enabled -eq 'True' -and $rule.Action -eq 'Allow' -and $ports.Protocol -eq 'TCP' -and $application.Program -eq 'Any' -and $service.Service -eq 'Any' -and $rule.Direction -eq 'Inbound' -and $rule.Profile -eq 'Any')
}
if ($needsFirewall) {
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        & $firewall -HttpsPort $HttpsPort -CertificatePort $CertificatePort -LocalAddress $LocalAddress
    } else {
        $argumentList = '-NoProfile -ExecutionPolicy Bypass -File "' + $firewall + '" -HttpsPort ' + $HttpsPort + ' -CertificatePort ' + $CertificatePort + ' -LocalAddress ' + $LocalAddress
        $process = Start-Process powershell.exe -Verb RunAs -WindowStyle Normal -Wait -PassThru -ArgumentList $argumentList
        if ($process.ExitCode -ne 0) { throw 'Approve the Windows prompt to allow phone access on the local subnet.' }
    }
}
Write-Host "Desktop: $env:PORTAL_PUBLIC_URL"
Write-Host "Phone certificate setup: http://${LocalAddress}:$CertificatePort"
Write-Host "Phone portal: https://${LocalAddress}:$HttpsPort"
Write-Host "Certificate SHA-256: $fingerprint"
Write-Host 'Install and trust the downloaded authority once on each phone. Keep both devices on the same private Wi-Fi.'

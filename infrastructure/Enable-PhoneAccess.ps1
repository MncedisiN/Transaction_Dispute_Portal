param(
    [int]$HttpsPort = 8443,
    [int]$CertificatePort = 8081,
    [Parameter(Mandatory = $true)][System.Net.IPAddress]$LocalAddress
)

$ErrorActionPreference = 'Stop'
if ($HttpsPort -lt 1024 -or $HttpsPort -gt 65535 -or $CertificatePort -lt 1024 -or $CertificatePort -gt 65535 -or $HttpsPort -eq $CertificatePort) {
    throw 'Invalid HTTPS or certificate port.'
}
$bytes = $LocalAddress.GetAddressBytes()
if ($bytes.Length -ne 4 -or -not ($bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168))) {
    throw 'Phone access requires a private IPv4 address.'
}

$rule = Get-NetFirewallRule -Name 'DisputePortal-AssessmentHttps' -ErrorAction SilentlyContinue
if ($rule) {
    $rule | Set-NetFirewallRule -Enabled True -Direction Inbound -Profile Any -Action Allow
    $rule | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program Any
    $rule | Get-NetFirewallServiceFilter | Set-NetFirewallServiceFilter -Service Any
    $rule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -LocalAddress $LocalAddress.IPAddressToString -RemoteAddress LocalSubnet
    $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort @($HttpsPort, $CertificatePort) -RemotePort Any
} else {
    New-NetFirewallRule -Name 'DisputePortal-AssessmentHttps' -DisplayName 'Dispute Portal Assessment HTTPS' -Direction Inbound -Action Allow -Program Any -Protocol TCP -LocalPort @($HttpsPort, $CertificatePort) -Profile Any -LocalAddress $LocalAddress.IPAddressToString -RemoteAddress LocalSubnet | Out-Null
}
Write-Host 'Dispute Portal phone access enabled for the selected ports on the local subnet.'

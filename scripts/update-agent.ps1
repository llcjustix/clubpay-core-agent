$ErrorActionPreference = 'Stop'

$bundleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExecutable = Join-Path $bundleDirectory 'ClubPay.Agent.Client.exe'
$installDirectory = 'C:\ClubPay\Agent'
$installedExecutable = Join-Path $installDirectory 'ClubPay.Agent.Client.exe'
$installedConfig = Join-Path $installDirectory 'appsettings.Local.json'

if (-not (Test-Path $sourceExecutable)) {
    throw "ClubPay Agent was not found next to the updater: $sourceExecutable"
}
if (-not (Test-Path $installedExecutable)) {
    throw 'Configured ClubPay Agent was not found. Use the per-PC first-install file from ClubPay Web Admin.'
}
if (-not (Test-Path $installedConfig)) {
    throw 'The Agent configuration is missing. Use the per-PC first-install file from ClubPay Web Admin.'
}

Write-Host 'Updating ClubPay Agent…' -ForegroundColor Cyan

# The enrollment is the identity of this physical PC. Back it up before files
# are replaced; the updater must never turn a configured kiosk into an
# unbound Agent.
$configBackup = Join-Path $env:TEMP ('clubpay-agent-config-' + [guid]::NewGuid().ToString() + '.json')
Copy-Item -Path $installedConfig -Destination $configBackup -Force
try {
    Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    Copy-Item -Path (Join-Path $bundleDirectory '*') -Destination $installDirectory -Recurse -Force
    Copy-Item -Path $configBackup -Destination $installedConfig -Force

    Start-Process -FilePath $installedExecutable
    Write-Host 'ClubPay Agent updated and started.' -ForegroundColor Green
}
finally {
    Remove-Item -Path $configBackup -Force -ErrorAction SilentlyContinue
}

Read-Host 'Press Enter to close'

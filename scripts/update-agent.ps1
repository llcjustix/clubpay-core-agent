param(
    [switch]$NoPrompt
)

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
$programBackup = Join-Path $env:TEMP ('clubpay-agent-program-' + [guid]::NewGuid().ToString())
Copy-Item -Path $installedConfig -Destination $configBackup -Force
New-Item -ItemType Directory -Path $programBackup -Force | Out-Null
Get-ChildItem -Path $installDirectory -Force | Where-Object {
    $_.Name -ne 'appsettings.Local.json'
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $programBackup -Recurse -Force
}
try {
    Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    Copy-Item -Path (Join-Path $bundleDirectory '*') -Destination $installDirectory -Recurse -Force
    Copy-Item -Path $configBackup -Destination $installedConfig -Force

    Start-Process -FilePath $installedExecutable
    Write-Host 'ClubPay Agent updated and started.' -ForegroundColor Green
}
catch {
    $updateError = $_
    Write-Host 'Agent update failed; restoring the previous build…' -ForegroundColor Yellow
    Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-ChildItem -Path $programBackup -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installDirectory -Recurse -Force
    }
    Copy-Item -Path $configBackup -Destination $installedConfig -Force
    Start-Process -FilePath $installedExecutable -ErrorAction SilentlyContinue
    throw $updateError
}
finally {
    Remove-Item -Path $configBackup -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $programBackup -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $NoPrompt) {
    Read-Host 'Press Enter to close'
}

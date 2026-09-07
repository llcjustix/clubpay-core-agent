param(
    [switch]$NoPrompt
)

$ErrorActionPreference = 'Stop'

$bundleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDirectory = 'C:\ClubPay\Manager'
$installedManager = Join-Path $installDirectory 'ClubPay.Agent.Admin.exe'
$installedControllerConfig = Join-Path $installDirectory 'Controller\controller.env'
$controllerUpdater = Join-Path $bundleDirectory 'Controller\update-windows.ps1'

if (-not (Test-Path (Join-Path $bundleDirectory 'ClubPay.Agent.Admin.exe'))) {
    throw 'ClubPay Manager was not found next to the updater.'
}
if (-not (Test-Path $installedManager) -or -not (Test-Path $installedControllerConfig)) {
    throw 'Configured ClubPay Manager was not found. Use the first-install file from ClubPay Web Admin.'
}
if (-not (Test-Path $controllerUpdater)) {
    throw 'The Manager package does not contain its Controller updater.'
}

Write-Host 'Updating ClubPay Manager…' -ForegroundColor Cyan

# Preserve the desktop build too. The nested Controller has its own atomic
# update and health-check rollback, while this backup protects the Manager UI
# if copying a new desktop build is interrupted.
$desktopBackup = Join-Path $env:TEMP ('clubpay-manager-desktop-' + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $desktopBackup -Force | Out-Null
Get-ChildItem -Path $installDirectory -Force | Where-Object {
    $_.Name -ne 'Controller'
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $desktopBackup -Recurse -Force
}

# The embedded Controller owns its own config, PostgreSQL data and runtime.
# Its updater preserves all three and restarts the scheduled local Controller.
try {
    Get-Process -Name 'ClubPay.Agent.Admin' -ErrorAction SilentlyContinue | Stop-Process -Force
    & $controllerUpdater -NoPrompt
    if ($LASTEXITCODE -ne 0) {
        throw 'The embedded Manager Controller did not finish updating.'
    }

# Copy only the desktop client files. The Controller folder is deliberately
# excluded because it was updated above with its data/config preserved.
    Get-ChildItem -Path $bundleDirectory -Force | Where-Object {
        $_.Name -notin @('Controller', 'controller-enrollment.json', 'install-manager.cmd', 'update-manager.cmd', 'update-manager.ps1')
    } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installDirectory -Recurse -Force
    }

    Start-Process -FilePath $installedManager
    Write-Host 'ClubPay Manager updated and started.' -ForegroundColor Green
}
catch {
    $updateError = $_
    Write-Host 'Manager update failed; restoring the previous desktop build…' -ForegroundColor Yellow
    Get-Process -Name 'ClubPay.Agent.Admin' -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-ChildItem -Path $desktopBackup -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installDirectory -Recurse -Force
    }
    Start-Process -FilePath $installedManager -ErrorAction SilentlyContinue
    throw $updateError
}
finally {
    Remove-Item -Path $desktopBackup -Recurse -Force -ErrorAction SilentlyContinue
}
if (-not $NoPrompt) {
    Read-Host 'Press Enter to close'
}

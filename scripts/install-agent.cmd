@echo off
setlocal EnableExtensions
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-agent.ps1"
if errorlevel 1 (
  echo.
  echo Installation did not finish. Read the message above and try again.
  pause
)

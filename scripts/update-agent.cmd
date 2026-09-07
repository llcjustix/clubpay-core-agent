@echo off
setlocal EnableExtensions
cd /d "%~dp0"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
  exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-agent.ps1"
if errorlevel 1 (
  echo.
  echo Update did not finish. Read the message above.
  pause
)

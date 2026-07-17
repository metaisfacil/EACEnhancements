@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0LaunchEacWithLogging.ps1"
if errorlevel 1 pause

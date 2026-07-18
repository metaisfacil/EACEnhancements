@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0TestBuildInstall.ps1"
set "LIVE_TEST_EXIT_CODE=%ERRORLEVEL%"
if not "%LIVE_TEST_EXIT_CODE%"=="0" pause
exit /b %LIVE_TEST_EXIT_CODE%

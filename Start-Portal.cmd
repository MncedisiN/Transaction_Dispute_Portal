@echo off
setlocal
title Start Dispute Portal
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Starting Dispute Portal...
echo Your browser will open when everything is ready.
echo.
set "PORTAL_BROWSER=-OpenBrowser"
if /i "%~1"=="/background" set "PORTAL_BROWSER="
where.exe pwsh.exe >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0infrastructure\Start-Portal.ps1" -SkipBuild %PORTAL_BROWSER%
) else (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0infrastructure\Start-Portal.ps1" -SkipBuild %PORTAL_BROWSER%
)
set "PORTAL_RESULT=%ERRORLEVEL%"
echo.
if not "%PORTAL_RESULT%"=="0" (
    echo The portal could not start. Read the message above, then try again.
)
if /i not "%~1"=="/background" pause
popd
exit /b %PORTAL_RESULT%

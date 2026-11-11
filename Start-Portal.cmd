@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Portal.ps1" -HttpsPort 5443 -CertificatePort 5177
pause

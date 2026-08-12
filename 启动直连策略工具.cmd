@echo off
setlocal
if exist "%~dp0Windows-Direct-Route-Fix.exe" (
    start "" "%~dp0Windows-Direct-Route-Fix.exe"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Windows-Direct-Route-Fix.ps1"
)
endlocal

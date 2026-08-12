@echo off
setlocal
if exist "%~dp0Windows-Direct-Route-Fix.exe" (
    "%~dp0Windows-Direct-Route-Fix.exe" --check-only-text
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Windows-Direct-Route-Fix.ps1" -CheckOnly
)
endlocal

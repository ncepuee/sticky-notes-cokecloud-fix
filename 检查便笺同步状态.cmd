@echo off
setlocal
if exist "%~dp0StickyNotes-CokeCloud-Fix.exe" (
    "%~dp0StickyNotes-CokeCloud-Fix.exe" --check-only
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0StickyNotes-CokeCloud-Fix-ASCII.ps1" -CheckOnly
)
endlocal

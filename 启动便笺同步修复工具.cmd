@echo off
setlocal
if exist "%~dp0StickyNotes-CokeCloud-Fix.exe" (
    start "" "%~dp0StickyNotes-CokeCloud-Fix.exe"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0StickyNotes-CokeCloud-Fix-ASCII.ps1"
)
endlocal

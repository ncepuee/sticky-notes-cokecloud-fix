@echo off
setlocal EnableExtensions

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo C# compiler was not found.
    exit /b 1
)

set "TEMPBUILD=%TEMP%\StickyNotesCokeCloudFix-build"
if not exist "%TEMPBUILD%" mkdir "%TEMPBUILD%"
set "TEMP_OUT=%TEMPBUILD%\StickyNotes-CokeCloud-Fix.exe"
set "OUT=%~dp0StickyNotes-CokeCloud-Fix.exe"
pushd "%TEMPBUILD%"
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:"%TEMP_OUT%" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0StickyNotesCokeCloudFix.cs"
set "EXITCODE=%ERRORLEVEL%"
popd

if "%EXITCODE%"=="0" (
    copy /Y "%TEMP_OUT%" "%OUT%" >nul
    echo Built: %OUT%
) else (
    echo Build failed with exit code %EXITCODE%.
)
exit /b %EXITCODE%

@echo off
setlocal EnableExtensions

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo C# compiler was not found.
    exit /b 1
)

set "TEMPBUILD=%TEMP%\WindowsDirectRouteFix-build"
if not exist "%TEMPBUILD%" mkdir "%TEMPBUILD%"
set "TEMP_OUT=%TEMPBUILD%\Windows-Direct-Route-Fix.exe"
set "SOURCE_DIR=%~dp0"
set "OUT=%~dp0Windows-Direct-Route-Fix.exe"
pushd "%TEMPBUILD%"
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:"%TEMP_OUT%" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%SOURCE_DIR%WindowsDirectRouteFix.cs"
set "EXITCODE=%ERRORLEVEL%"
popd

if "%EXITCODE%"=="0" (
    copy /Y "%TEMP_OUT%" "%OUT%" >nul
    echo Built: %OUT%
) else (
    echo Build failed with exit code %EXITCODE%.
)
exit /b %EXITCODE%

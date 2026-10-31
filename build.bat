@echo off
rem Builds "C Drive Cleaner.exe" using the C# compiler that comes with Windows - nothing to install.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  exit /b 1
)

if not exist "src\app.ico" powershell -NoProfile -ExecutionPolicy Bypass -File "src\make-icon.ps1" -Out "src\app.ico"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 ^
  /out:"C Drive Cleaner.exe" /win32manifest:src\app.manifest /win32icon:src\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)
echo.
echo Built "C Drive Cleaner.exe"

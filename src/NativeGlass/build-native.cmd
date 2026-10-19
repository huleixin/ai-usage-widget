@echo off
setlocal
pushd "%~dp0"
if defined VSCMD_VER goto compile
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo Visual Studio 2022 C++ Build Tools and Windows SDK are required.
  exit /b 1
)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%i"
if not defined VSROOT exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
:compile
cl.exe /nologo /std:c++20 /O2 /EHsc /MT /LD /DWIN32_LEAN_AND_MEAN /DNOMINMAX LiquidGlassNative.cpp /link /OUT:LiquidGlassNative.dll /Brepro user32.lib dwmapi.lib CoreMessaging.lib windowsapp.lib d2d1.lib dxguid.lib
exit /b %errorlevel%

@echo off
setlocal
rem Usage: build.bat [Release|Debug] [notest] [noshaders]
set "CFG=Release"
set "NOTEST=" & set "NOSHADERS="
for %%A in (%*) do (
  if /i "%%A"=="notest" set "NOTEST=1"
  if /i "%%A"=="noshaders" set "NOSHADERS=1"
  if /i "%%A"=="Debug" set "CFG=Debug"
  if /i "%%A"=="Release" set "CFG=Release"
)

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
if not exist "%ROOT%\KSPVE.sln" for %%I in ("%ROOT%\..") do set "ROOT=%%~fI"
if not exist "%ROOT%\KSPVE.sln" ( echo KSPVE.sln not found. & exit /b 1 )

set "DOTNET=dotnet"
if exist "E:\Tools\dotnet\dotnet.exe" set "DOTNET=E:\Tools\dotnet\dotnet.exe"
if defined KSPVE_DOTNET set "DOTNET=%KSPVE_DOTNET%"

set "UNITY=C:\Program Files\Unity\Hub\Editor\2019.4.41f1\Editor\Unity.exe"
if defined UNITY_EXE set "UNITY=%UNITY_EXE%"

set "OUT=%ROOT%\out"
set "SHADEROUT=%OUT%\shaders"
set "BUNDLE=%SHADEROUT%\kspve.shaderbundle"
set "BIN=%ROOT%\src\KSPVE\bin\%CFG%\net48"

if not defined KSPDIR set "KSPDIR=D:\SteamLibrary\steamapps\common\ModTestingKSP"
if not exist "%KSPDIR%\KSP_x64_Data\Managed\UnityEngine.dll" echo WARNING: KSP assemblies not found under "%KSPDIR%".

echo === [1/3] Build ===
"%DOTNET%" build "%ROOT%\KSPVE.sln" -c %CFG% --nologo -p:KSPDIR="%KSPDIR%"
if errorlevel 1 ( echo Build failed. & exit /b 1 )

if not defined NOTEST (
  "%DOTNET%" test "%ROOT%\tests\KSPVE.Pure.Tests\KSPVE.Pure.Tests.csproj" -c %CFG% --no-build --nologo
  if errorlevel 1 ( echo Unit tests failed. & exit /b 1 )
)

echo === [2/3] Shaders ===
if defined NOSHADERS (
  echo Skipped.
  goto package
)
if not exist "%UNITY%" ( echo Unity not found: %UNITY% & exit /b 1 )
if not exist "%SHADEROUT%" mkdir "%SHADEROUT%"
set "LOG=%SHADEROUT%\unity-build.log"
del /q "%BUNDLE%" "%BUNDLE%.contents.txt" "%LOG%" 2>nul

rem No -nographics: Unity needs a real D3D11 device to validate compute kernels.
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%ROOT%\unity\KSPVEShaders" -executeMethod KSPVE.Build.BuildBundles.BuildAll -kspveOut "%SHADEROUT%" -logFile "%LOG%"
set "UCODE=%errorlevel%"
if "%UCODE%"=="0" if exist "%BUNDLE%" (
  echo Shader bundle OK: %BUNDLE%
  goto package
)
echo Unity build FAILED [exit code %UCODE%]. Last log lines:
if exist "%LOG%" (
  powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 40"
  findstr /i /r /c:"No valid Unity Editor license" /c:"not been activated with a valid License" /c:"Failed to activate/update license" /c:"LICENSE SYSTEM" "%LOG%" >nul && (
    echo Unity needs its free Personal licence activated: sign in once via Unity Hub.
    exit /b 2
  )
)
exit /b 1

:package
echo === [3/3] Package ===
if not exist "%BUNDLE%" ( echo Missing %BUNDLE%; run without "noshaders" first. & exit /b 1 )
if not exist "%BIN%\KSPVE.dll" ( echo Missing KSPVE.dll in %BIN%. & exit /b 1 )
if not exist "%BIN%\KSPVE.Pure.dll" ( echo Missing KSPVE.Pure.dll in %BIN%. & exit /b 1 )

set "STAGE=%OUT%\package\GameData\KSPVE"
if exist "%OUT%\package" rmdir /s /q "%OUT%\package"
mkdir "%STAGE%\Plugins" "%STAGE%\Shaders"
xcopy "%ROOT%\gamedata\*" "%STAGE%\" /e /i /y /q >nul
copy /y "%BIN%\KSPVE.dll" "%STAGE%\Plugins\" >nul
copy /y "%BIN%\KSPVE.Pure.dll" "%STAGE%\Plugins\" >nul
copy /y "%BUNDLE%" "%STAGE%\Shaders\" >nul
copy /y "%ROOT%\LICENSE" "%STAGE%\LICENSE.txt" >nul

set "VER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$v=(Get-Content '%STAGE%\KSPVE.version' -Raw | ConvertFrom-Json).VERSION; '{0}.{1}.{2}' -f $v.MAJOR,$v.MINOR,$v.PATCH"`) do set "VER=%%V"
if not defined VER ( echo Could not read version from KSPVE.version. & exit /b 1 )

set "ZIP=%OUT%\KSPVE-%VER%.zip"
del /q "%ZIP%" 2>nul
pushd "%OUT%\package"
tar -a -c -f "%ZIP%" GameData
set "TCODE=%errorlevel%"
popd
if not "%TCODE%"=="0" ( echo Zip failed. & exit /b 1 )

echo Packaged %ZIP%
exit /b 0
@echo off
setlocal
rem Usage: test.bat [Release|Debug] [nodeploy]
rem Env: KSPDIR (KSP folder), SOURCE_SAVE (default "Test Save")
set "CFG=Release"
set "NODEPLOY="
for %%A in (%*) do (
  if /i "%%A"=="nodeploy" set "NODEPLOY=1"
  if /i "%%A"=="Debug" set "CFG=Debug"
  if /i "%%A"=="Release" set "CFG=Release"
)

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
if not exist "%ROOT%\KSPVE.sln" for %%I in ("%ROOT%\..") do set "ROOT=%%~fI"
if not exist "%ROOT%\KSPVE.sln" ( echo KSPVE.sln not found. & exit /b 1 )

set "KSP=%KSPDIR%"
if not defined KSP set "KSP=D:\SteamLibrary\steamapps\common\ModTestingKSP"
set "SRCSAVE=%SOURCE_SAVE%"
if not defined SRCSAVE set "SRCSAVE=Test Save"

if not exist "%KSP%\KSP_x64.exe" ( echo KSP not found at "%KSP%". & exit /b 1 )
tasklist /fi "imagename eq KSP_x64.exe" | find /i "KSP_x64.exe" >nul && ( echo KSP is running; close it first. & exit /b 1 )

if defined NODEPLOY goto selftest

echo === Deploy ===
set "BIN=%ROOT%\src\KSPVE\bin\%CFG%\net48"
if not exist "%BIN%\KSPVE.dll" ( echo Missing build output; run build.bat first. & exit /b 1 )
if not exist "%BIN%\KSPVE.Pure.dll" ( echo Missing build output; run build.bat first. & exit /b 1 )
set "TARGET=%KSP%\GameData\KSPVE"
mkdir "%TARGET%\Plugins" 2>nul
copy /y "%BIN%\KSPVE.dll" "%TARGET%\Plugins\" >nul
copy /y "%BIN%\KSPVE.Pure.dll" "%TARGET%\Plugins\" >nul
xcopy "%ROOT%\gamedata\*" "%TARGET%\" /e /i /y /q >nul
if exist "%ROOT%\out\shaders\kspve.shaderbundle" (
  mkdir "%TARGET%\Shaders" 2>nul
  copy /y "%ROOT%\out\shaders\kspve.shaderbundle" "%TARGET%\Shaders\" >nul
) else (
  echo WARNING: no shader bundle at out\shaders; run build.bat to include KSPVE shaders.
)
echo Deployed to %TARGET%

:selftest
echo === Self-test ===
set "BENCH=%KSP%\saves\KSPVE_Bench"
if not exist "%BENCH%\persistent.sfs" (
  if not exist "%KSP%\saves\%SRCSAVE%\persistent.sfs" ( echo Source save not found: %KSP%\saves\%SRCSAVE%\persistent.sfs & exit /b 1 )
  mkdir "%BENCH%" 2>nul
  copy /y "%KSP%\saves\%SRCSAVE%\persistent.sfs" "%BENCH%\persistent.sfs" >nul
  echo Created saves\KSPVE_Bench from "%SRCSAVE%" [source untouched].
)

set "REPORT=%KSP%\GameData\KSPVE\PluginData\selftest\report.json"
del /q "%REPORT%" 2>nul

pushd "%KSP%"
start "" /wait "%KSP%\KSP_x64.exe" -force-d3d11 -kspve-selftest
popd

if not exist "%REPORT%" (
  echo KSP exited without writing %REPORT%.
  echo It may have been relaunched through Steam without -kspve-selftest. Check KSP.log for [KSPVE] lines.
  exit /b 1
)

powershell -NoProfile -Command "$r=Get-Content '%REPORT%' -Raw | ConvertFrom-Json; Write-Host ('Self-test ' + $(if($r.passed){'PASSED'}else{'FAILED'}) + '. Stages: ' + (($r.stages | %% { $_.stage }) -join ', ')); $r.screenshots | %% { Write-Host ('  screenshot: ' + $_) }; $r.failures | %% { Write-Host ('  failure: ' + $_) }; $r.kspveErrors | %% { Write-Host ('  KSPVE error: ' + $_) }; $r.configIssues | %% { Write-Host ('  config: ' + $_) }; if(-not $r.passed){ exit 1 }"
exit /b %errorlevel%
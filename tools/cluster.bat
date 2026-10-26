@echo off
setlocal enabledelayedexpansion
rem Usage: cluster.bat full | lite | status      (no argument = status)
rem full: every KSS2 system. lite: Kcalbeloh, Kerr, Sol, Nova Kirbani + Proksimus only.
rem Lite parks SystemAethera/Kormin/Orbel/Seereon in GameData_KSPVE_parked\KSS2.
rem Principia saves do not carry between layouts: keep one save per layout.
set "LAYOUT=%~1"
if "%LAYOUT%"=="" set "LAYOUT=status"
if /i not "%LAYOUT%"=="full" if /i not "%LAYOUT%"=="lite" if /i not "%LAYOUT%"=="status" (
  echo Usage: cluster.bat full ^| lite ^| status
  exit /b 1
)

set "KSP=%KSPDIR%"
if not defined KSP set "KSP=D:\SteamLibrary\steamapps\common\ModTestingKSP"
set "KSS2=%KSP%\GameData\KSS2"
set "PARKED=%KSP%\GameData_KSPVE_parked\KSS2"
if not exist "%KSS2%" ( echo KSS2 not found at %KSS2%. & exit /b 1 )

set "NI=0" & set "NP=0" & set "NM=0"
for %%S in (SystemAethera SystemKormin SystemOrbel SystemSeereon) do (
  set "ST_%%S=missing"
  set "IN=" & set "AWAY="
  if exist "%KSS2%\%%S" set "IN=1"
  if exist "%PARKED%\%%S" set "AWAY=1"
  if defined IN if defined AWAY ( echo %%S is in both GameData and %PARKED%; remove one copy by hand. & exit /b 1 )
  if defined IN ( set "ST_%%S=installed" & set /a NI+=1 )
  if defined AWAY ( set "ST_%%S=parked" & set /a NP+=1 )
  if "!ST_%%S!"=="missing" set /a NM+=1
)

set "CURRENT=mixed"
if %NI%==4 set "CURRENT=full"
if %NP%==4 set "CURRENT=lite"

if /i "%LAYOUT%"=="status" (
  echo Cluster layout: %CURRENT%
  for %%S in (SystemAethera SystemKormin SystemOrbel SystemSeereon) do echo   %%S  !ST_%%S!
  if "%CURRENT%"=="mixed" echo WARNING: neither layout applies until all four are installed ^(full^) or all parked ^(lite^).
  exit /b 0
)

tasklist /fi "imagename eq KSP_x64.exe" | find /i "KSP_x64.exe" >nul && ( echo KSP is running; close it before switching. & exit /b 1 )
if %NM% gtr 0 ( echo Some systems are in neither GameData nor the parked folder. Run "cluster.bat status". & exit /b 1 )
if /i "%CURRENT%"=="%LAYOUT%" ( echo Already %LAYOUT%. & exit /b 0 )

mkdir "%PARKED%" 2>nul
for %%S in (SystemAethera SystemKormin SystemOrbel SystemSeereon) do (
  if /i "%LAYOUT%"=="lite" if "!ST_%%S!"=="installed" move "%KSS2%\%%S" "%PARKED%\%%S" >nul
  if /i "%LAYOUT%"=="full" if "!ST_%%S!"=="parked" move "%PARKED%\%%S" "%KSS2%\%%S" >nul
)
echo Switched to the %LAYOUT% layout. Start a save made in this layout.
exit /b 0
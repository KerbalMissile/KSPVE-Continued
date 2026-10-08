<#
.SYNOPSIS  Copies the packaged GameData\VolumetricBlackholeFx into a KSP install (run tools\package.ps1 first).
#>
param([string]$KspDir = $env:KSPDIR)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $KspDir) { $KspDir = "E:\SteamLibrary\steamapps\common\Kerbal Space Program" }
if (-not (Test-Path (Join-Path $KspDir "KSP_x64.exe"))) { throw "KSP not found at '$KspDir'." }
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw "KSP is running; close it before deploying." }

$mod = Join-Path $root "GameData\VolumetricBlackholeFx"
if (-not (Test-Path (Join-Path $mod "Plugins\VolumetricBlackholeFx.dll"))) { throw "Run tools\package.ps1 first." }
$target = Join-Path $KspDir "GameData\VolumetricBlackholeFx"
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem $mod | Where-Object { $_.Name -ne "PluginData" } | Copy-Item -Destination $target -Recurse -Force
Write-Host "Deployed to $target"

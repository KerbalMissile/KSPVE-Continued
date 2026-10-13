param(
    [string]$Configuration = "Release",
    [string]$KspDir = $env:KSPDIR
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $KspDir) { $KspDir = "E:\SteamLibrary\steamapps\common\Kerbal Space Program" }
if (-not (Test-Path (Join-Path $KspDir "KSP_x64.exe"))) { throw "KSP not found at '$KspDir'." }
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw "KSP is running; close it before deploying." }

$bin = Join-Path $root "src\KSPVE\bin\$Configuration\net48"
foreach ($f in "KSPVE.dll", "KSPVE.Pure.dll") {
    if (-not (Test-Path (Join-Path $bin $f))) { throw "Missing build output $f in $bin; run tools\build.ps1 first." }
}

# KSPVE only ever writes inside GameData\KSPVE.
$target = Join-Path $KspDir "GameData\KSPVE"
$plugins = Join-Path $target "Plugins"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item (Join-Path $bin "KSPVE.dll"), (Join-Path $bin "KSPVE.Pure.dll") $plugins -Force
Copy-Item (Join-Path $root "gamedata\*") $target -Recurse -Force

# Shader bundle, built by tools\build-shaders.ps1 (requires the Unity 2019.4.18f1 editor and licence).
$bundle = Join-Path $root "out\shaders\kspve.shaderbundle"
if (Test-Path $bundle) {
    $shaders = Join-Path $target "Shaders"
    New-Item -ItemType Directory -Force $shaders | Out-Null
    Copy-Item $bundle $shaders -Force
} else {
    Write-Warning "No shader bundle at $bundle; run tools\build-shaders.ps1 to include KSPVE shaders."
}
Write-Host "Deployed KSPVE to $target"

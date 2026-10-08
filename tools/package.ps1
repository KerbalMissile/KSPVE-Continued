<#
.SYNOPSIS  Zips GameData\KSPVE (plugin, shader bundle, configs, licence) for a release: out\KSPVE-<version>.zip.
.NOTES     Run tools\build.ps1 and tools\build-shaders.ps1 first.
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$bin = Join-Path $root "src\KSPVE\bin\$Configuration\net48"
$dlls = "KSPVE.dll", "KSPVE.Pure.dll"
foreach ($f in $dlls) { if (-not (Test-Path (Join-Path $bin $f))) { throw "Missing $f in $bin; run tools\build.ps1 first." } }
$bundle = Join-Path $root "out\shaders\kspve.shaderbundle"
if (-not (Test-Path $bundle)) { throw "Missing $bundle; run tools\build-shaders.ps1 first." }

# The mod folder, laid out as in GameData: the repo's gamedata folder plus the build outputs.
$stage = Join-Path $root "out\package\GameData\KSPVE"
Remove-Item (Join-Path $root "out\package") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "Plugins"), (Join-Path $stage "Shaders") | Out-Null
Copy-Item (Join-Path $root "gamedata\*") $stage -Recurse -Force
foreach ($f in $dlls) { Copy-Item (Join-Path $bin $f) (Join-Path $stage "Plugins") -Force }
Copy-Item $bundle (Join-Path $stage "Shaders") -Force
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt") -Force

$v = (Get-Content (Join-Path $stage "KSPVE.version") -Raw | ConvertFrom-Json).VERSION
$zip = Join-Path $root "out\KSPVE-$($v.MAJOR).$($v.MINOR).$($v.PATCH).zip"
Remove-Item $zip -ErrorAction SilentlyContinue

# Entries as GameData/KSPVE/..., with forward slashes so every unzipper (and CKAN) reads them.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = "GameData/KSPVE/" + $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
Write-Host "Packaged $zip"

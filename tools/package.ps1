<#
.SYNOPSIS  Assembles GameData\VolumetricBlackholeFx (plugin, shader bundle, configs, licence) and zips it for a release.
.NOTES     Run tools\build.ps1 and tools\build-shaders.ps1 first. Writes out\VolumetricBlackholeFx-<version>.zip.
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$mod = Join-Path $root "GameData\VolumetricBlackholeFx"

$bin = Join-Path $root "src\VolumetricBlackholeFx\bin\$Configuration\net48"
$dlls = "VolumetricBlackholeFx.dll", "VolumetricBlackholeFx.Pure.dll"
foreach ($f in $dlls) { if (-not (Test-Path (Join-Path $bin $f))) { throw "Missing $f in $bin; run tools\build.ps1 first." } }
$bundle = Join-Path $root "out\shaders\volumetricblackholefx.shaderbundle"
if (-not (Test-Path $bundle)) { throw "Missing $bundle; run tools\build-shaders.ps1 first." }

New-Item -ItemType Directory -Force (Join-Path $mod "Plugins"), (Join-Path $mod "Shaders") | Out-Null
foreach ($f in $dlls) { Copy-Item (Join-Path $bin $f) (Join-Path $mod "Plugins") -Force }
Copy-Item $bundle (Join-Path $mod "Shaders") -Force
Copy-Item (Join-Path $root "LICENSE") (Join-Path $mod "LICENSE.txt") -Force

$v = (Get-Content (Join-Path $mod "VolumetricBlackholeFx.version") -Raw | ConvertFrom-Json).VERSION
$version = "$($v.MAJOR).$($v.MINOR).$($v.PATCH)"
$out = Join-Path $root "out"
New-Item -ItemType Directory -Force $out | Out-Null
$zip = Join-Path $out "VolumetricBlackholeFx-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue

# The zip holds GameData\VolumetricBlackholeFx\..., with forward slashes so every unzipper (and CKAN) reads it.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $mod -Recurse -File | Where-Object { $_.FullName -notmatch '\\PluginData\\' } | ForEach-Object {
        $entry = "GameData/VolumetricBlackholeFx/" + $_.FullName.Substring($mod.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
Write-Host "Packaged $zip"

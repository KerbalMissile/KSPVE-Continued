<#
.SYNOPSIS  Builds the plugin (and runs the unit tests unless -NoTest).
#>
param(
    [string]$Configuration = "Release",
    [switch]$NoTest
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dotnet = if ($env:DOTNET) { $env:DOTNET } elseif (Test-Path "E:\Tools\dotnet\dotnet.exe") { "E:\Tools\dotnet\dotnet.exe" } else { "dotnet" }

& $dotnet build (Join-Path $root "VolumetricBlackholeFx.sln") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $NoTest) {
    & $dotnet test (Join-Path $root "tests\VolumetricBlackholeFx.Pure.Tests\VolumetricBlackholeFx.Pure.Tests.csproj") -c $Configuration --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}
Write-Host "Build OK ($Configuration)."

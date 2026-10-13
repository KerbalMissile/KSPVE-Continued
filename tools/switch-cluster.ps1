<#
.SYNOPSIS  Switches the Sol + Kcalbeloh + KSS2 cluster between its full and lite layouts.
.DESCRIPTION
    full: every KSS2 system (Kcalbeloh, Kerr, Sol, Nova Kirbani with Proksimus, Aethera, Kormin, Orbel, Seereon).
    lite: Kcalbeloh, Kerr, Sol and Nova Kirbani with Proksimus only (less to load: faster, less memory).
    The lite layout leaves KSS2's SystemAethera, SystemKormin, SystemOrbel and SystemSeereon folders out of GameData:
    this script moves them to GameData_KSPVE_parked next to GameData (and back). KSPVE's two layouts each apply only
    when their own set of folders is installed, so a half-switched install loads neither; the script refuses to
    leave one behind. ModuleManager re-patches on the next start (a slower first load after each switch).
    Principia keeps the whole system in each save: a save from one layout will not load in the other, so keep one
    save per layout.
.EXAMPLE   tools\switch-cluster.ps1 lite
.EXAMPLE   tools\switch-cluster.ps1 status
#>
param(
    [ValidateSet('full', 'lite', 'status')]
    [string]$Layout = 'status',
    [string]$KspDir = $env:KSPDIR
)
$ErrorActionPreference = 'Stop'
if (-not $KspDir) { $KspDir = 'E:\SteamLibrary\steamapps\common\Kerbal Space Program' }
$kss2 = Join-Path $KspDir 'GameData\KSS2'
$parked = Join-Path $KspDir 'GameData_KSPVE_parked\KSS2'
$systems = 'SystemAethera', 'SystemKormin', 'SystemOrbel', 'SystemSeereon'
if (-not (Test-Path $kss2)) { throw "KSS2 not found at $kss2." }

function Where-Is($name) {
    $installed = Test-Path (Join-Path $kss2 $name)
    $away = Test-Path (Join-Path $parked $name)
    if ($installed -and $away) { throw "$name is both in GameData and in $parked; remove one copy by hand." }
    if ($installed) { 'installed' } elseif ($away) { 'parked' } else { 'missing' }
}

$state = @{}
foreach ($s in $systems) { $state[$s] = Where-Is $s }
$current = if (@($systems | Where-Object { $state[$_] -eq 'installed' }).Count -eq $systems.Count) { 'full' }
           elseif (@($systems | Where-Object { $state[$_] -eq 'parked' }).Count -eq $systems.Count) { 'lite' }
           else { 'mixed' }

if ($Layout -eq 'status') {
    Write-Host "Cluster layout: $current"
    foreach ($s in $systems) { Write-Host ("  {0,-14} {1}" -f $s, $state[$s]) }
    if ($current -eq 'mixed') { Write-Warning 'Neither layout applies until all four are installed (full) or all parked (lite).' }
    return
}
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw 'KSP is running; close it before switching.' }
$missing = @($systems | Where-Object { $state[$_] -eq 'missing' })
if ($missing.Count) { throw "Not found in GameData or parked: $($missing -join ', ')." }
if ($current -eq $Layout) { Write-Host "Already $Layout."; return }

New-Item -ItemType Directory -Force $parked | Out-Null
foreach ($s in $systems) {
    if ($Layout -eq 'lite' -and $state[$s] -eq 'installed') { Move-Item (Join-Path $kss2 $s) (Join-Path $parked $s) }
    if ($Layout -eq 'full' -and $state[$s] -eq 'parked') { Move-Item (Join-Path $parked $s) (Join-Path $kss2 $s) }
}
Write-Host "Switched to the $Layout layout. Start a save made in this layout (Principia saves do not carry over)."

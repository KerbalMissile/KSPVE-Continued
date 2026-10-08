<#
.SYNOPSIS  Builds kspve.shaderbundle with Unity 2019.4.18f1 in batchmode.
.NOTES     Exit codes: 0 success, 1 failure, 2 Unity licence problem.
#>
param(
    [string]$UnityExe = $(if ($env:UNITY_EXE) { $env:UNITY_EXE } else { 'E:\Tools\Unity\2019.4.18f1\Editor\Unity.exe' }),
    [string]$ProjectPath = '',
    [string]$OutDir = '',
    [int]$TimeoutMinutes = 30
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) { $ProjectPath = Join-Path $repo 'unity\KSPVEShaders' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'out\shaders' }

if (-not (Test-Path $UnityExe)) { Write-Host "Unity not found: $UnityExe"; exit 1 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$ProjectPath = (Resolve-Path $ProjectPath).Path
$log = Join-Path $OutDir 'unity-build.log'
$bundle = Join-Path $OutDir 'kspve.shaderbundle'
$contents = "$bundle.contents.txt"
Remove-Item -ErrorAction SilentlyContinue $bundle, $contents, $log

# No -nographics: Unity needs a real D3D11 device for ComputeShader.HasKernel to validate kernels.
$argList = @('-batchmode', '-quit',
    '-projectPath', "`"$ProjectPath`"",
    '-executeMethod', 'KSPVE.Build.BuildBundles.BuildAll',
    '-kspveOut', "`"$OutDir`"",
    '-logFile', "`"$log`"")
Write-Host "Running Unity (timeout $TimeoutMinutes min)..."
$p = Start-Process -FilePath $UnityExe -ArgumentList $argList -PassThru
$timedOut = $false
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    try { $p.Kill() } catch {}
    Write-Host "Unity timed out after $TimeoutMinutes minutes."
    $timedOut = $true
}
$code = -1
if (-not $timedOut) { $code = $p.ExitCode }

if ((-not $timedOut) -and ($code -eq 0) -and (Test-Path $bundle)) {
    $size = (Get-Item $bundle).Length
    Write-Host "[KSPVE build] OK: $bundle ($size bytes)"
    if (Test-Path $contents) { Write-Host "Contents ($contents):"; Get-Content $contents }
    exit 0
}

Write-Host "[KSPVE build] FAILED (Unity exit code $code)."
$logText = ''
if (Test-Path $log) {
    Write-Host "Last 40 log lines ($log):"
    Get-Content $log -Tail 40
    $logText = Get-Content $log -Raw
}
if ($logText -match 'No valid Unity Editor license|not been activated with a valid License|Failed to activate/update license|LICENSE SYSTEM|Serial number|com\.unity\.editor\.headless') {
    Write-Host 'Unity needs its free Personal licence activated: sign in once via Unity Hub or by opening the Unity editor.'
    exit 2
}
exit 1

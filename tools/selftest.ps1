param(
    [string]$KspDir = $env:KSPDIR,
    [string]$SourceSave = "Test Save",
    [int]$TimeoutMinutes = 20
)
$ErrorActionPreference = "Stop"
if (-not $KspDir) { $KspDir = "E:\SteamLibrary\steamapps\common\Kerbal Space Program" }
$exe = Join-Path $KspDir "KSP_x64.exe"
if (-not (Test-Path $exe)) { throw "KSP not found at '$KspDir'." }
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw "KSP is already running; close it first." }

# The benchmark save is a copy; the source save is never modified.
$bench = Join-Path $KspDir "saves\KSPVE_Bench"
if (-not (Test-Path (Join-Path $bench "persistent.sfs"))) {
    $src = Join-Path $KspDir "saves\$SourceSave\persistent.sfs"
    if (-not (Test-Path $src)) { throw "Source save not found: $src" }
    New-Item -ItemType Directory -Force $bench | Out-Null
    Copy-Item $src (Join-Path $bench "persistent.sfs")
    Write-Host "Created saves\KSPVE_Bench from '$SourceSave' (source untouched)."
}

$report = Join-Path $KspDir "GameData\KSPVE\PluginData\selftest\report.json"
Remove-Item $report -ErrorAction SilentlyContinue

$start = Get-Date
$p = Start-Process -FilePath $exe -ArgumentList "-force-d3d11", "-kspve-selftest" -WorkingDirectory $KspDir -PassThru
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Stop-Process -Id $p.Id -Force
    throw "Self-test timed out after $TimeoutMinutes minutes."
}
$seconds = [int]((Get-Date) - $start).TotalSeconds

if (-not (Test-Path $report)) {
    $hint = if ($seconds -lt 60) { " KSP exited very quickly; it may have been relaunched through Steam without the -kspve-selftest argument." } else { "" }
    throw "KSP exited after ${seconds}s without writing $report.$hint Check KSP.log for [KSPVE] lines."
}

$r = Get-Content $report -Raw | ConvertFrom-Json
$verdict = if ($r.passed) { "PASSED" } else { "FAILED" }
Write-Host ("Self-test {0} in {1}s. Stages: {2}" -f $verdict, $seconds, (($r.stages | ForEach-Object { $_.stage }) -join ", "))
foreach ($s in $r.screenshots) { Write-Host "  screenshot: $s" }
foreach ($f in $r.failures) { Write-Host "  failure: $f" }
foreach ($e in $r.kspveErrors) { Write-Host "  KSPVE error: $e" }
foreach ($c in $r.configIssues) { Write-Host "  config: $c" }
if (-not $r.passed) { exit 1 }

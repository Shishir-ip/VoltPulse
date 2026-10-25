# VoltPulse soak test: runs the app for a given duration and records memory/CPU/handles,
# then reports the trend. A 24-hour soak is part of the release checklist (see QA.md);
# a shorter run can be used for quick regression checks.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\soak.ps1 [-Hours 24] [-ExePath path]
param(
    [double]$Hours = 24,
    [int]$IntervalSeconds = 60,
    [string]$ExePath = "$PSScriptRoot\..\src\VoltPulse.App\bin\Release\net8.0-windows\VoltPulse.exe",
    [string]$CsvPath = "$PSScriptRoot\soak-results.csv"
)

$ErrorActionPreference = "Stop"
$durationSeconds = [int]($Hours * 3600)

if (-not (Test-Path $ExePath)) {
    Write-Error "VoltPulse.exe not found at $ExePath - build first (dotnet publish or build.ps1)."
}

$existing = Get-Process -Name VoltPulse -ErrorAction SilentlyContinue
if ($existing) { Write-Error "VoltPulse is already running; stop it before soaking." }

Start-Process -FilePath $ExePath | Out-Null
Start-Sleep -Seconds 10
$proc = Get-Process -Name VoltPulse -ErrorAction SilentlyContinue
if (-not $proc) { Write-Error "VoltPulse did not start." }

"sampling for $Hours hour(s), every $IntervalSeconds s -> $CsvPath"
"timestamp,working_set_mb,private_mb,cpu_seconds,handles"
$csv = New-Object System.Collections.Generic.List[string]
$first = $null

$deadline = (Get-Date).AddSeconds($durationSeconds)
while ((Get-Date) -lt $deadline) {
    $proc.Refresh()
    if ($proc.HasExited) { Write-Error "VoltPulse exited unexpectedly during the soak!" }
    $wsMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    $privMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    $cpu = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1)
    $handles = $proc.HandleCount
    $line = "$(Get-Date -Format o),$wsMb,$privMb,$cpu,$handles"
    $csv.Add($line)
    if ($null -eq $first) { $first = @{ ws = $wsMb; cpu = $cpu } }
    Write-Host $line
    Start-Sleep -Seconds $IntervalSeconds
}

$csv | Set-Content $CsvPath
$proc.Refresh()
$lastWs = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$lastCpu = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1)
$avgCpuPerHour = [math]::Round(($lastCpu - $first.cpu) / $Hours, 1)

Stop-Process -Name VoltPulse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== soak summary ==="
Write-Host "Working set: $first.ws MB -> $lastWs MB"
Write-Host "Average CPU: $avgCpuPerHour s/hour (~$([math]::Round($avgCpuPerHour / 3600 * 100, 2))% of one core)"
Write-Host "Results: $CsvPath"
if ($lastWs -gt ($first.ws * 2 + 100)) {
    Write-Warning "Working set more than doubled - investigate for leaks!"
} else {
    Write-Host "Memory growth looks acceptable."
}

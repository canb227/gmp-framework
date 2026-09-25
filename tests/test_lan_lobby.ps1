<#
.SYNOPSIS
    Headless multi-instance LAN lobby test for GMPFramework.

.DESCRIPTION
    Spawns N Godot instances (1 host + N-1 joiners) in headless mode,
    waits for all to exit, and reports aggregate pass/fail based on
    exit codes and stdout markers.

.PARAMETER GodotPath
    Path to the Godot console binary. Defaults to the local 4.7.2 install.

.PARAMETER PeerCount
    Total number of peers including the host. Default 3.

.PARAMETER HostPort
    Port the host listens on. Default 2272.

.PARAMETER TestTimeout
    Per-instance timeout in seconds. Default 30.

.PARAMETER ScriptTimeout
    How long the script waits before force-killing all instances. Default 45.
#>
param(
    [string]$GodotPath = "C:\Users\steph\OneDrive\Documents\godot\Godot_v4.7.2-stable_mono_win64_console.exe",
    [int]$PeerCount = 3,
    [int]$HostPort = 2272,
    [int]$TestTimeout = 30,
    [int]$ScriptTimeout = 45,
    [switch]$Game
)

$ErrorActionPreference = "Stop"

if ($Game) {
    if (-not $PSBoundParameters.ContainsKey('TestTimeout')) { $TestTimeout = 60 }
    if (-not $PSBoundParameters.ContainsKey('ScriptTimeout')) { $ScriptTimeout = 80 }
}
$testFlag = if ($Game) { @("--test-game") } else { @("--test") }

$projectDir = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $GodotPath)) {
    Write-Error "Godot binary not found at: $GodotPath"
    exit 1
}

Write-Host "=== LAN Lobby Headless Test ===" -ForegroundColor Cyan
Write-Host "  Godot:     $GodotPath"
Write-Host "  Peers:     $PeerCount (1 host + $($PeerCount - 1) joiners)"
Write-Host "  Host port: $HostPort"
Write-Host "  Timeout:   ${TestTimeout}s per instance, ${ScriptTimeout}s script"
Write-Host "  Mode:      $(if ($Game) { 'game' } else { 'lobby' })"
Write-Host ""

$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }

$processes = @()

# --- Host instance ---
$hostLog = Join-Path $logDir "host.log"
$hostArgs = @(
    "--headless"
    "--path", $projectDir
    "--"
    "--lan", "--name", "Host"
    "--host", $HostPort.ToString()
) + $testFlag + @(
    "--expect-peers", $PeerCount.ToString()
    "--test-timeout", $TestTimeout.ToString()
)
$hostErrLog = Join-Path $logDir "host_err.log"
Write-Host "Starting HOST on port $HostPort..." -ForegroundColor Yellow
$hostProc = Start-Process -FilePath $GodotPath -ArgumentList $hostArgs `
    -RedirectStandardOutput $hostLog -RedirectStandardError $hostErrLog `
    -NoNewWindow -PassThru
$processes += @{ Name = "Host"; Proc = $hostProc; Log = $hostLog; ErrLog = $hostErrLog }

Start-Sleep -Seconds 2

# --- Joiner instances ---
for ($i = 1; $i -lt $PeerCount; $i++) {
    $peerName = "Peer$($i + 1)"
    $listenPort = 9100 + $i
    $peerLog = Join-Path $logDir "$peerName.log"

    $peerArgs = @(
        "--headless"
        "--path", $projectDir
        "--"
        "--lan", "--name", $peerName
        "--join", "127.0.0.1:$HostPort"
        "--port", $listenPort.ToString()
    ) + $testFlag + @(
        "--expect-peers", $PeerCount.ToString()
        "--test-timeout", $TestTimeout.ToString()
    )
    $peerErrLog = Join-Path $logDir "${peerName}_err.log"
    Write-Host "Starting $peerName (listen $listenPort, joining 127.0.0.1:$HostPort)..." -ForegroundColor Yellow
    $peerProc = Start-Process -FilePath $GodotPath -ArgumentList $peerArgs `
        -RedirectStandardOutput $peerLog -RedirectStandardError $peerErrLog `
        -NoNewWindow -PassThru
    $processes += @{ Name = $peerName; Proc = $peerProc; Log = $peerLog; ErrLog = $peerErrLog }

    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "All $PeerCount instances launched. Waiting up to ${ScriptTimeout}s..." -ForegroundColor Cyan

# --- Wait for all processes with a global timeout ---
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$allDone = $false

while ($sw.Elapsed.TotalSeconds -lt $ScriptTimeout) {
    $allDone = $true
    foreach ($entry in $processes) {
        if (-not $entry.Proc.HasExited) {
            $allDone = $false
            break
        }
    }
    if ($allDone) { break }
    Start-Sleep -Seconds 1
}

# --- Kill stragglers ---
foreach ($entry in $processes) {
    if (-not $entry.Proc.HasExited) {
        Write-Host "KILLING $($entry.Name) (did not exit in time)" -ForegroundColor Red
        try { $entry.Proc.Kill() } catch {}
    }
}

# --- Collect results ---
Write-Host ""
Write-Host "=== Results ===" -ForegroundColor Cyan

$allPassed = $true
foreach ($entry in $processes) {
    $entry.Proc.WaitForExit()
    $code = $entry.Proc.ExitCode
    $logContent = if (Test-Path $entry.Log) { Get-Content $entry.Log -Raw } else { "" }
    $errContent = if (Test-Path $entry.ErrLog) { Get-Content $entry.ErrLog -Raw } else { "" }

    $testLine = ($logContent -split "`n" | Where-Object { $_ -match "TEST_RESULT:" }) | Select-Object -Last 1

    $exceptionLines = @($logContent, $errContent) | ForEach-Object { $_ -split "`n" } |
        Where-Object { $_ -match "Unhandled exception|System\.[A-Za-z.]*Exception" }

    $passed = ($testLine -match "PASS") -and -not $exceptionLines
    if ($passed) {
        Write-Host "  $($entry.Name): PASS (exit $code)" -ForegroundColor Green
    } else {
        Write-Host "  $($entry.Name): FAIL (exit $code)" -ForegroundColor Red
        if ($testLine) { Write-Host "    $testLine" -ForegroundColor DarkGray }
        if ($exceptionLines) { $exceptionLines | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
        $allPassed = $false
    }
}

# --- Consensus: every instance must report identical consensus_<name>=<value> tokens ---
$consensus = @{}
foreach ($entry in $processes) {
    $logContent = if (Test-Path $entry.Log) { Get-Content $entry.Log -Raw } else { "" }
    $testLine = ($logContent -split "`n" | Where-Object { $_ -match "TEST_RESULT:" }) | Select-Object -Last 1
    foreach ($m in [regex]::Matches([string]$testLine, "consensus_(\w+)=(\S+)")) {
        $key = $m.Groups[1].Value
        if (-not $consensus.ContainsKey($key)) { $consensus[$key] = @{} }
        $consensus[$key][$entry.Name] = $m.Groups[2].Value
    }
}
foreach ($key in $consensus.Keys) {
    $values = @($consensus[$key].Values | Sort-Object -Unique)
    $missing = $processes.Count - $consensus[$key].Count
    if ($values.Count -ne 1 -or $missing -gt 0) {
        $detail = ($consensus[$key].GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ", "
        Write-Host "  CONSENSUS MISMATCH on ${key}: $detail" -ForegroundColor Red
        $allPassed = $false
    } else {
        Write-Host "  consensus ${key}: $($values[0])" -ForegroundColor Green
    }
}

Write-Host ""
if ($allPassed) {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "SOME TESTS FAILED - see tests/logs/ for details" -ForegroundColor Red
    exit 1
}

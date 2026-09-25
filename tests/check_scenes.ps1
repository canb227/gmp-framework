# Loads every scene/resource headlessly; fails on load errors or missing dependencies.
param([string]$GodotPath = "C:\Users\steph\OneDrive\Documents\godot\Godot_v4.7.2-stable_mono_win64_console.exe")
$projectDir = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
$log = Join-Path $logDir "check_scenes.log"
& $GodotPath --headless --path $projectDir --script res://tests/check_scenes.gd *> $log
$content = Get-Content $log
$bad = $content | Where-Object { $_ -match "CHECK_SCENES_FAIL|Cannot open file|Failed loading resource|Unable to load|missing|Parse Error" }
$content | Where-Object { $_ -match "^CHECK_SCENES " } | Write-Host
if ($bad) { $bad | Select-Object -First 30 | Write-Host -ForegroundColor Red }
if (($content -match "CHECK_SCENES_RESULT:PASS") -and -not $bad) { Write-Host "ALL SCENES LOADED" -ForegroundColor Green; exit 0 }
Write-Host "SCENE CHECK FAILED - see tests/logs/check_scenes.log" -ForegroundColor Red; exit 1

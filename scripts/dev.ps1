<#
.SYNOPSIS
  Dev loop: close the game, build + deploy the plugin, relaunch through Steam, follow the log.

.EXAMPLE
  .\scripts\dev.ps1              # build, deploy, launch, follow log (Ctrl+C to stop following)
  .\scripts\dev.ps1 -NoLaunch    # build + deploy only
  .\scripts\dev.ps1 -All         # show the whole BepInEx log, not just this mod's lines
#>
param(
    [switch]$NoLaunch,
    [switch]$All
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$proj = Join-Path $root 'src\DeskrawlQoL\DeskrawlQoL.csproj'
$steamAppId = 4623570

$gameDir = (dotnet msbuild $proj -getProperty:GameDir).Trim()
if (-not (Test-Path (Join-Path $gameDir 'Deskrawl.exe'))) { throw "Game not found at '$gameDir'. See README > Development." }

# The plugin DLL is locked while the game runs, so close it first.
$game = Get-Process Deskrawl -ErrorAction SilentlyContinue
if ($game) {
    Write-Host "Closing Deskrawl..."
    $game | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    foreach ($p in $game) { if (-not $p.WaitForExit(10000)) { $p | Stop-Process -Force } }
    Start-Sleep -Seconds 1
}

dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($NoLaunch) { return }

$log = Join-Path $gameDir 'BepInEx\LogOutput.log'
$launched = Get-Date
Write-Host "Launching Deskrawl via Steam..."
Start-Process "steam://rungameid/$steamAppId"

# Wait for BepInEx to start a fresh log, then follow it.
while (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $launched) { Start-Sleep -Milliseconds 500 }
Write-Host "Following $log (Ctrl+C to stop)" -ForegroundColor DarkGray
$filter = if ($All) { '.' } else { 'Deskrawl QoL|Error|Fatal|Exception|Chainloader startup complete' }
Get-Content $log -Wait | Where-Object { $_ -match $filter } | ForEach-Object {
    $color = if ($_ -match 'Error|Fatal|Exception') { 'Red' } elseif ($_ -match 'Warning') { 'Yellow' } else { 'Gray' }
    Write-Host $_ -ForegroundColor $color
}

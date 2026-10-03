<#
.SYNOPSIS
  Builds the release zips into dist\:
    DeskrawlQoL-vX.Y.Z.zip              full install: pinned BepInEx + preconfigured BepInEx.cfg + plugin
    DeskrawlQoL-vX.Y.Z-plugin-only.zip  just BepInEx\plugins\DeskrawlQoL.dll (for updating)

  BepInEx is downloaded from builds.bepinex.dev (cached in .cache\) and verified against a pinned
  SHA-256, so the release never depends on what happens to be in your local game folder.
  Generated folders (interop, cache, unity-libs) are never included: they are derived from the
  game's files and every player's BepInEx regenerates them on first launch.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Compress-Archive on Windows PowerShell writes backslash paths, which some unzip tools mishandle.
function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath }
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = (Resolve-Path $SourceDir).Path.TrimEnd('\') + '\'
        Get-ChildItem $SourceDir -Recurse -File -Force | ForEach-Object {
            $entry = $_.FullName.Substring($base.Length).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose() }
}

$root = Split-Path $PSScriptRoot
$proj = Join-Path $root 'src\DeskrawlQoL\DeskrawlQoL.csproj'

# Pinned BepInEx build. Only bump after testing the new build in game (see README > Development).
$bepUrl = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'
$bepSha256 = 'f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a'

$version = (dotnet msbuild $proj -getProperty:Version).Trim()
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$cache = Join-Path $root '.cache'
New-Item -ItemType Directory -Force $dist, $cache | Out-Null
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

# 1. Build the plugin (without touching the game install).
dotnet build $proj -c Release -p:DeployToGame=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $root 'src\DeskrawlQoL\bin\Release\net6.0\DeskrawlQoL.dll'

# 2. Fetch + verify BepInEx.
$bepZip = Join-Path $cache 'BepInEx-be.788.zip'
if (-not (Test-Path $bepZip)) {
    Write-Host "Downloading BepInEx..."
    Invoke-WebRequest $bepUrl -OutFile $bepZip
}
$hash = (Get-FileHash $bepZip -Algorithm SHA256).Hash.ToLower()
if ($hash -ne $bepSha256) { Remove-Item $bepZip; throw "BepInEx zip hash mismatch ($hash). Deleted it; re-run to download again." }

# 3. Full package.
$full = Join-Path $stage 'full'
Expand-Archive $bepZip $full
New-Item -ItemType Directory -Force (Join-Path $full 'BepInEx\config'), (Join-Path $full 'BepInEx\plugins') | Out-Null
Copy-Item (Join-Path $root 'packaging\BepInEx.cfg') (Join-Path $full 'BepInEx\config\BepInEx.cfg')
Copy-Item $dll (Join-Path $full 'BepInEx\plugins\')
Copy-Item (Join-Path $root 'README.md') (Join-Path $full 'DeskrawlQoL-README.md')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $full 'DeskrawlQoL-LICENSE.txt')
Copy-Item (Join-Path $root 'packaging\third-party\BepInEx-LICENSE.txt') (Join-Path $full 'BepInEx\BepInEx-LICENSE.txt')

$fullZip = Join-Path $dist "DeskrawlQoL-v$version.zip"
New-Zip $full $fullZip

# 4. Plugin-only package.
$lite = Join-Path $stage 'lite'
New-Item -ItemType Directory -Force (Join-Path $lite 'BepInEx\plugins') | Out-Null
Copy-Item $dll (Join-Path $lite 'BepInEx\plugins\')
$liteZip = Join-Path $dist "DeskrawlQoL-v$version-plugin-only.zip"
New-Zip $lite $liteZip

Remove-Item -Recurse -Force $stage
Get-Item $fullZip, $liteZip | Format-Table Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } }

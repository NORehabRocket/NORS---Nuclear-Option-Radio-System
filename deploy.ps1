<#
  Builds the NORS plugin and copies it into the game's BepInEx plugins folder.
  Only the three required DLLs are deployed (the build output also contains netstandard
  facade shims from the Concentus package that must NOT be shipped into a Mono plugin folder).
#>
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Nuclear Option",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pluginProj = Join-Path $root "src\NORS.Plugin\NORS.Plugin.csproj"
$dest = Join-Path $GameDir "BepInEx\plugins\NORS"
$bin = Join-Path $root "src\NORS.Plugin\bin\$Configuration"

Write-Host "Building NORS plugin..." -ForegroundColor Cyan
dotnet build $pluginProj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }

New-Item -ItemType Directory -Force -Path $dest | Out-Null

# Remove a stale Concentus.dll left by older builds (no longer used).
$stale = Join-Path $dest "Concentus.dll"
if (Test-Path $stale) { Remove-Item $stale -Force; Write-Host "  - removed stale Concentus.dll" }

# Allowlist: ONLY these belong in the plugin folder.
$files = @("NORS.dll", "NORS.Common.dll")
foreach ($f in $files) {
    $src = Join-Path $bin $f
    if (-not (Test-Path $src)) { throw "Missing build output: $src" }
    Copy-Item $src -Destination $dest -Force
    Write-Host "  + $f"
}

$meta = Join-Path $root "meta.json"
if (Test-Path $meta) { Copy-Item $meta -Destination $dest -Force; Write-Host "  + meta.json" }

Write-Host "Deployed NORS to $dest" -ForegroundColor Green
Write-Host "Remember to run the relay server (publish-server.ps1) and set ServerHost in the config." -ForegroundColor Yellow

<#
  Builds the NORS plugin (Release) and packages a release zip for distribution (NOMNOM / GitHub).
  The zip contains a single top-level "NORS" folder that drops straight into BepInEx\plugins\,
  with ONLY the allowlisted files (the build output also contains netstandard facade shims that
  must NOT be shipped into a Mono plugin folder).

  Output: dist\NORS-<version>.zip
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pluginProj = Join-Path $root "src\NORS.Plugin\NORS.Plugin.csproj"
$bin = Join-Path $root "src\NORS.Plugin\bin\$Configuration"

# Version from meta.json so the zip name tracks the release.
$meta = Get-Content (Join-Path $root "meta.json") -Raw | ConvertFrom-Json
$version = $meta.version

Write-Host "Building NORS plugin v$version..." -ForegroundColor Cyan
dotnet build $pluginProj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "pkg\NORS"
if (Test-Path (Join-Path $dist "pkg")) { Remove-Item (Join-Path $dist "pkg") -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# Allowlist: ONLY these belong in the plugin folder, plus the release docs.
$required = @("NORS.dll", "NORS.Common.dll")
foreach ($f in $required) {
    $src = Join-Path $bin $f
    if (-not (Test-Path $src)) { throw "Missing build output: $src" }
    Copy-Item $src -Destination $stage -Force
    Write-Host "  + NORS\$f"
}
Copy-Item (Join-Path $root "meta.json") -Destination $stage -Force; Write-Host "  + NORS\meta.json"
foreach ($doc in @("README.md", "CHANGELOG.md", "GUIDE.md", "RELEASE_NOTES.md", "LICENSE")) {
    $p = Join-Path $root $doc
    if (Test-Path $p) { Copy-Item $p -Destination $stage -Force; Write-Host "  + NORS\$doc" }
}

$zip = Join-Path $dist "NORS-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -Force
Remove-Item (Join-Path $dist "pkg") -Recurse -Force

# NOMNOM's manifest registers the release asset by its exact file name ("1NORS.zip"), so emit a
# copy under that name — THIS is the file to upload as the GitHub release asset.
$asset = Join-Path $dist "1NORS.zip"
Copy-Item $zip -Destination $asset -Force

Write-Host "Packaged $zip" -ForegroundColor Green
Write-Host "Release asset (upload this one): $asset" -ForegroundColor Green
Get-Item $zip, $asset | Select-Object Name, @{N='SizeKB';E={[math]::Round($_.Length/1KB,1)}}

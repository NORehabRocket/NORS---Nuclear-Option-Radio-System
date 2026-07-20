<#
  Publishes the standalone NORS relay server as self-contained executables (no .NET runtime needed
  on the host). The console relay is cross-platform; the WinForms admin GUI is Windows-only.

    dist\server\<rid>\NORS.Server(.exe)     - console relay (interactive or --headless)
    dist\server-gui\NORS.ServerGUI.exe      - windowed admin UI (Windows only)

  Runtimes default to all supported platforms; pass -Runtimes to narrow it.
#>
param(
    [string[]]$Runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [string]$Configuration = "Release",
    [switch]$ConsoleOnly
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$consoleProj = Join-Path $root "src\NORS.Server\NORS.Server.csproj"
$guiProj = Join-Path $root "src\NORS.ServerGUI\NORS.ServerGUI.csproj"

function Publish($proj, $rid, $out) {
    Write-Host "Publishing $(Split-Path $proj -Leaf) -> $rid" -ForegroundColor Cyan
    dotnet publish $proj -c $Configuration -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $proj ($rid)" }
}

foreach ($rid in $Runtimes) {
    Publish $consoleProj $rid (Join-Path $root "dist\server\$rid")
}

# The admin GUI uses WinForms, so it only targets Windows.
if (-not $ConsoleOnly -and ($Runtimes -contains "win-x64")) {
    Publish $guiProj "win-x64" (Join-Path $root "dist\server-gui")
}

Write-Host "Done." -ForegroundColor Green
Write-Host "Windows GUI:     .\dist\server-gui\NORS.ServerGUI.exe" -ForegroundColor Yellow
Write-Host "Windows console: .\dist\server\win-x64\NORS.Server.exe --port 5555 --name `"My NORS`"" -ForegroundColor Yellow
Write-Host "Linux console:   ./dist/server/linux-x64/NORS.Server --port 5555 --name 'My NORS'" -ForegroundColor Yellow
Write-Host "macOS console:   ./dist/server/osx-arm64/NORS.Server --port 5555 --name 'My NORS'" -ForegroundColor Yellow
Write-Host "Open UDP 5555 on the host firewall / router so others can connect." -ForegroundColor Yellow

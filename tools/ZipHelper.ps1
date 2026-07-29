<#
  Spec-correct zip creation.

  Windows PowerShell 5.1's Compress-Archive writes entry names with BACKSLASH
  separators ("NORS\NORS.dll"). The ZIP spec (APPNOTE 4.4.17.1) requires forward
  slashes, and the difference is not cosmetic:

    - On Linux/Proton, "\" is a legal filename character, so the archive extracts
      as ONE file literally named  NORS\NORS.dll  instead of  NORS/NORS.dll  —
      no folder is created, and NORS.dll can never find NORS.Common.dll.
    - Some Windows tools silently "fix" it, which is why it looked fine for us
      and broke for others depending on which extractor they used.

  New-ModZip writes entry names itself, always with "/", so the archive is valid
  everywhere. Reported by the community (Lomb(otomy), Wheat, nat, Maelle) — thanks.
#>

function New-ModZip {
    param(
        # Folder whose CONTENTS become the archive root (include the top-level
        # mod folder inside this, e.g. stage\NORS\... produces NORS/... entries).
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

    $root = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path $ZipPath -Parent) | Out-Null

    $fs = [IO.File]::Open($ZipPath, [IO.FileMode]::CreateNew)
    try {
        $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in Get-ChildItem -Path $root -Recurse -File) {
                $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
                $entry = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]$f.LastWriteTime
                $es = $entry.Open()
                try {
                    $bytes = [IO.File]::ReadAllBytes($f.FullName)
                    $es.Write($bytes, 0, $bytes.Length)
                }
                finally { $es.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $fs.Dispose() }
}

function Test-ModZip {
    <# Fails loudly if an archive ever regresses to backslash separators. #>
    param([Parameter(Mandatory = $true)][string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $z = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $bad = @($z.Entries | Where-Object { $_.FullName.Contains([char]92) })
        if ($bad.Count -gt 0) {
            throw "INVALID ZIP: $($bad.Count) entr(y/ies) use backslash separators, e.g. '$($bad[0].FullName)'"
        }
        Write-Host ("  zip OK - {0} entries, all '/' separators" -f $z.Entries.Count) -ForegroundColor Green
    }
    finally { $z.Dispose() }
}

#requires -Version 3.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"

# Always rebuild before packing: previously pack.ps1 could package a stale exe
# left over from an earlier build (dist\rll.exe and the exe inside the zip had
# different hashes because they came from two separate builds).
Write-Host "Rebuilding before packaging..."
& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "Pre-pack build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}

$exe = Join-Path $dist "rll.exe"
if (-not (Test-Path $exe)) {
    throw "rll.exe not found after build."
}

# Single source of truth for the version: the compiled assembly metadata.
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
$version = ($fileVersion -split "\.")[0..2] -join "."
$name = "RLL-$version"
Write-Host ("Version: " + $version + " (from assembly metadata)")

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("rll-pack-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    # Runtime package: exe plus docs only.
    $runDir = Join-Path $staging "run"
    New-Item -ItemType Directory -Path $runDir | Out-Null
    Copy-Item $exe (Join-Path $runDir "rll.exe")
    Copy-Item (Join-Path $root "README.md") (Join-Path $runDir "README.md")
    Copy-Item (Join-Path $root "SECURITY.md") (Join-Path $runDir "SECURITY.md")
    Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $runDir "CHANGELOG.md")
    # Put the checksum of the exe INSIDE the package too: a hash that only lives
    # next to the download proves nothing if the download itself was replaced.
    $exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Set-Content -Path (Join-Path $runDir "rll.exe.sha256") -Value ($exeHash + "  rll.exe") -Encoding ASCII

    $runZip = Join-Path $dist ($name + "-win-anycpu.zip")
    if (Test-Path $runZip) { Remove-Item $runZip -Force }
    Compress-Archive -Path (Join-Path $runDir "*") -DestinationPath $runZip -CompressionLevel Optimal

    # Source package: sources, tests and build scripts.
    $srcDir = Join-Path $staging "src"
    New-Item -ItemType Directory -Path $srcDir | Out-Null
    Copy-Item (Join-Path $root "src") $srcDir -Recurse
    Copy-Item (Join-Path $root "tests") $srcDir -Recurse
    Copy-Item (Join-Path $root "build.ps1") $srcDir
    Copy-Item (Join-Path $root "test.ps1") $srcDir
    Copy-Item (Join-Path $root "uitest.ps1") $srcDir
    Copy-Item (Join-Path $root "e2e.ps1") $srcDir
    Copy-Item (Join-Path $root "pack.ps1") $srcDir
    Copy-Item (Join-Path $root "README.md") $srcDir
    Copy-Item (Join-Path $root "SECURITY.md") $srcDir
    Copy-Item (Join-Path $root "CHANGELOG.md") $srcDir
    # Include every delivery note and user guide, not just the one whose name
    # happens to match a hardcoded version (previously only "*1.3.0*.md" was
    # picked up, so a 1.4.x release silently shipped the 1.3.0 note and left
    # the newer ones out of the source package).
    foreach ($doc in Get-ChildItem $root -Filter "*.md") {
        Copy-Item -LiteralPath $doc.FullName -Destination $srcDir
    }
    $srcZip = Join-Path $dist ($name + "-source.zip")
    if (Test-Path $srcZip) { Remove-Item $srcZip -Force }
    Compress-Archive -Path (Join-Path $srcDir "*") -DestinationPath $srcZip -CompressionLevel Optimal

    # Checksums.
    $sums = @()
    $sums += $exeHash + "  rll.exe"
    $sums += (Get-FileHash $runZip -Algorithm SHA256).Hash + "  " + (Split-Path -Leaf $runZip)
    $sums += (Get-FileHash $srcZip -Algorithm SHA256).Hash + "  " + (Split-Path -Leaf $srcZip)
    Set-Content -Path (Join-Path $dist "SHA256SUMS.txt") -Value ($sums -join "`r`n") -Encoding ASCII

    Write-Host ("Packed " + (Split-Path -Leaf $runZip))
    Write-Host ("Packed " + (Split-Path -Leaf $srcZip))
}
finally {
    if (Test-Path $staging) {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

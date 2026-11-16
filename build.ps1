#requires -Version 3.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $framework)) {
    $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
}
$csc = Join-Path $framework "csc.exe"
if (-not (Test-Path $csc)) {
    throw "csc.exe not found."
}
if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}

# Collect every source file under src/ recursively. A single rule avoids the
# drift that previously existed between build.ps1 (explicit UI list), test.ps1
# (Core only) and uitest.ps1 (another explicit list): adding a UI file no
# longer requires editing more than one script.
$sources = Get-ChildItem $src -Recurse -Filter "*.cs" |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Web.Extensions.dll",
    "System.IO.Compression.dll",
    "System.IO.Compression.FileSystem.dll",
    "System.Net.Http.dll",
    "System.Security.dll"
) | ForEach-Object { "/reference:" + (Join-Path $framework $_) }

$output = Join-Path $dist "rll.exe"

# Regenerate the program icon if it is missing, then embed it.
# The icon is produced by tools/IconGen.cs (vector drawing, no third-party assets).
$icon = Join-Path $src "app.ico"
if (-not (Test-Path $icon)) {
    $iconGen = Join-Path $root "tools\IconGen.cs"
    if (Test-Path $iconGen) {
        if (-not (Test-Path (Join-Path $root "build"))) {
            New-Item -ItemType Directory -Path (Join-Path $root "build") | Out-Null
        }
        Write-Host "Generating application icon..."
        & $csc /nologo /target:exe /platform:anycpu /langversion:5 /codepage:65001 `
            ("/out:" + (Join-Path $root "build\IconGen.exe")) `
            ("/reference:" + (Join-Path $framework "System.dll")) `
            ("/reference:" + (Join-Path $framework "System.Drawing.dll")) `
            $iconGen
        if ($LASTEXITCODE -ne 0) {
            throw "Icon generation failed with exit code $LASTEXITCODE"
        }
        & (Join-Path $root "build\IconGen.exe") $icon
    }
}

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/langversion:5",
    "/optimize+",
    "/codepage:65001",
    ("/win32manifest:" + (Join-Path $src "app.manifest")),
    ("/out:" + $output)
) + $references + $sources
if (Test-Path $icon) {
    $arguments += ("/win32icon:" + $icon)
}

Write-Host "Compiling $output"
& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}
Write-Host ("Build OK: " + $output + " (" + (Get-Item $output).Length + " bytes)")
Write-Host ("Version: " + (Get-Item $output).VersionInfo.FileVersion)

#requires -Version 3.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$test = Join-Path $root "tests\UiSmoke.cs"
$out = Join-Path $root "build\test"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $framework)) {
    $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
}
$csc = Join-Path $framework "csc.exe"
if (-not (Test-Path $csc)) {
    throw "csc.exe not found."
}
if (-not (Test-Path $out)) {
    New-Item -ItemType Directory -Path $out | Out-Null
}

# Same recursive collection as build.ps1: a new UI file must not require
# editing this list (that drift previously caused "build OK but uitest fails").
$sources = Get-ChildItem $src -Recurse -Filter "*.cs" |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }
$sources += $test

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

$output = Join-Path $out "UiSmoke.exe"
$arguments = @(
    "/nologo",
    "/target:exe",
    "/platform:anycpu",
    "/langversion:5",
    "/optimize+",
    "/codepage:65001",
    "/main:LiteLauncher.UiSmoke.UiSmoke",
    ("/out:" + $output)
) + $references + $sources

Write-Host "Compiling UI smoke test $output"
& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "UI smoke compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Running UI smoke tests"
& $output @args
if ($LASTEXITCODE -ne 0) {
    throw "UI smoke tests failed with exit code $LASTEXITCODE"
}

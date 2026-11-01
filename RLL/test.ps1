#requires -Version 3.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\Core"
$tests = Join-Path $root "tests\TestHarness.cs"
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

$sources = @()
$sources += Get-ChildItem $src -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
$sources += $tests

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Web.Extensions.dll",
    "System.IO.Compression.dll",
    "System.IO.Compression.FileSystem.dll",
    "System.Net.Http.dll",
    "System.Security.dll"
) | ForEach-Object { "/reference:" + (Join-Path $framework $_) }

$output = Join-Path $out "TestHarness.exe"
$arguments = @(
    "/nologo",
    "/target:exe",
    "/platform:anycpu",
    "/langversion:5",
    "/optimize+",
    "/codepage:65001",
    ("/out:" + $output)
) + $references + $sources

Write-Host "Compiling $output"
& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Running tests"
& $output @args
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

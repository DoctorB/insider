[CmdletBinding()]
param(
    [string] $PackageDirectory = "artifacts/Insider-windows-x64"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $PackageDirectory))

if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) {
    throw "Package directory not found: '$packagePath'."
}

$requiredFiles = @(
    "insider.dll",
    "insider.deps.json",
    "insider.runtimeconfig.json",
    "Insider.Installation.dll",
    "README.txt",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "bundle/core/Insider.Abstractions.dll",
    "bundle/core/Insider.Loader.dll",
    "bundle/core/Insider.Bootstrap.dll",
    "bundle/native/win-x64/version.dll"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $packagePath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package file is missing: '$relativePath'."
    }

    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required package file is empty: '$relativePath'."
    }
}

$forbiddenNames = @(
    "Insider.Tests.dll",
    "Insider.PluginFixture.dll",
    "Insider.DependencyFixture.dll"
)
$unexpectedFiles = Get-ChildItem -LiteralPath $packagePath -Recurse -File | Where-Object {
    $forbiddenNames -contains $_.Name -or $_.Extension -in @(".cs", ".csproj", ".sln", ".slnx")
}

if ($unexpectedFiles.Count -gt 0) {
    $relativePaths = $unexpectedFiles | ForEach-Object {
        $_.FullName.Substring($packagePath.Length + 1)
    }
    throw "Package contains test or source files: $($relativePaths -join ', ')."
}

$helpOutput = & dotnet (Join-Path $packagePath "insider.dll") --help 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Packaged CLI smoke test failed with exit code $LASTEXITCODE.`n$($helpOutput -join [Environment]::NewLine)"
}

if (-not (($helpOutput -join [Environment]::NewLine).Contains("Insider Mod Loader CLI"))) {
    throw "Packaged CLI smoke test returned unexpected output."
}

Write-Host "Verified Insider package at $packagePath"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $NativeBootstrap,

    [Parameter(Mandatory = $true)]
    [string] $CliPublishDirectory,

    [string] $Configuration = "Release",

    [string] $OutputDirectory = "artifacts/Insider-windows-x64"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$nativePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $NativeBootstrap))
$cliPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $CliPublishDirectory))
$artifactsPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $outputPath.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must remain below '$artifactsRoot'."
}

if (-not (Test-Path -LiteralPath $nativePath -PathType Leaf)) {
    throw "Native bootstrap not found: '$nativePath'."
}

if (-not (Test-Path -LiteralPath $cliPath -PathType Container)) {
    throw "Published CLI directory not found: '$cliPath'."
}

$coreFiles = @(
    "Insider.Abstractions.dll",
    "Insider.Loader.dll",
    "Insider.Bootstrap.dll"
)

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

$bundleCore = Join-Path $outputPath "bundle/core"
$bundleNative = Join-Path $outputPath "bundle/native/win-x64"
New-Item -ItemType Directory -Path $outputPath, $bundleCore, $bundleNative -Force | Out-Null

Get-ChildItem -LiteralPath $cliPath -Force | Copy-Item -Destination $outputPath -Recurse -Force
Copy-Item -LiteralPath $nativePath -Destination (Join-Path $bundleNative "version.dll")

foreach ($file in $coreFiles) {
    $source = Join-Path $repositoryRoot "src/$($file.Replace('.dll', ''))/bin/$Configuration/netstandard2.0/$file"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Managed core file not found: '$source'. Build the solution first."
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $bundleCore $file)
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging/README.txt") -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md") -Destination $outputPath
Write-Host "Created Insider package at $outputPath"

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
    "bundle/core/Insider.Hooking.dll",
    "bundle/core/Mono.Cecil.dll",
    "bundle/core/Mono.Cecil.Mdb.dll",
    "bundle/core/Mono.Cecil.Pdb.dll",
    "bundle/core/Mono.Cecil.Rocks.dll",
    "bundle/core/MonoMod.Backports.dll",
    "bundle/core/MonoMod.Core.dll",
    "bundle/core/MonoMod.Iced.dll",
    "bundle/core/MonoMod.ILHelpers.dll",
    "bundle/core/MonoMod.RuntimeDetour.dll",
    "bundle/core/MonoMod.Utils.dll",
    "bundle/core/System.Reflection.Emit.ILGeneration.dll",
    "bundle/core/System.Reflection.Emit.Lightweight.dll",
    "bundle/native/win-x64/version.dll",
    "bundle/runtime/win-x64/hostfxr.dll",
    "bundle/runtime/win-x64/hostpolicy.dll",
    "bundle/runtime/win-x64/coreclr.dll",
    "bundle/runtime/win-x64/System.Private.CoreLib.dll",
    "bundle/runtime/win-x64/Insider.Il2CppHost.exe",
    "bundle/runtime/win-x64/Insider.Il2CppHost.dll",
    "bundle/runtime/win-x64/Insider.Il2CppHost.deps.json",
    "bundle/runtime/win-x64/Insider.Il2CppHost.runtimeconfig.json"
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

$thirdPartyHashes = [ordered]@{
    "bundle/core/Mono.Cecil.dll" = "831DCA77470D85CB6FFBEA3072DAA7A3DF5B7C9FCFD9C3F43674A9BE99D4BFCF"
    "bundle/core/Mono.Cecil.Mdb.dll" = "28CB367972BDC1CD43E4006306AF2FD96D37F4ED4B239EE90E1DC7237A93AF7F"
    "bundle/core/Mono.Cecil.Pdb.dll" = "A332332633FBCB20E8D50E49B4DB7BD1557721417122CF0C5F4C42F2332391D0"
    "bundle/core/Mono.Cecil.Rocks.dll" = "BF992F3DCE364EBCC3200FA7832EF07E20B4E2DBC3A8A6213CE44E3D239DB984"
    "bundle/core/MonoMod.Backports.dll" = "1018A3604A8143913BF4A60AC9FE78050AFE4F91D2581CEA1A37AAEF9F3549F2"
    "bundle/core/MonoMod.Core.dll" = "6A05EC34323C12D2F5CEBA3E7343BCEE1479CBB66D41CAA4D6EA5A082C6ACF19"
    "bundle/core/MonoMod.Iced.dll" = "44A209E110CDF59ED92975050BE34A03C7ADA3CE281326B57F61660CBBC7FB70"
    "bundle/core/MonoMod.ILHelpers.dll" = "D478BCF2E03337E14526C6DCFA8EDF0F5C653FE4E08ED9512F27CB9652CBA2E3"
    "bundle/core/MonoMod.RuntimeDetour.dll" = "708E9BC593FE76A30F70468BF77981A11C8C45C1FC266E208904856557ADDF31"
    "bundle/core/MonoMod.Utils.dll" = "E181D3ABA8CA8EB2C5CF1A3F6A3BCFA9DFFD5B302DBDFA43A4BBE866CF8ED498"
    "bundle/core/System.Reflection.Emit.ILGeneration.dll" = "CAC0339E1222085FF8BF1E5225F4AA9559A1CE15B6CF5C2E1F65A3B4EF496A86"
    "bundle/core/System.Reflection.Emit.Lightweight.dll" = "60C01BA12B3C03EAC692D14B6F1CE69900BF7425C4DAC487B99AB7DCAA9D7287"
}

foreach ($entry in $thirdPartyHashes.GetEnumerator()) {
    $path = Join-Path $packagePath $entry.Key
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if (-not $actual.Equals($entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Third-party package hash mismatch for '$($entry.Key)'."
    }
}

$forbiddenNames = @(
    "Insider.Tests.dll",
    "Insider.DiagnosticFixture.dll",
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

$helpText = $helpOutput -join [Environment]::NewLine
$requiredHelpText = @(
    "Insider Mod Loader CLI",
    "diagnose <game.exe>",
    "plugins disable",
    "plugins enable",
    "plugins disabled"
)

foreach ($expectedText in $requiredHelpText) {
    if (-not $helpText.Contains($expectedText)) {
        throw "Packaged CLI help is missing '$expectedText'."
    }
}

Write-Host "Verified Insider package at $packagePath"

[CmdletBinding()]
param(
    [string] $PackageDirectory = "artifacts/Insider-windows-x64",

    [string] $FixtureExecutable = "artifacts/native-build/Insider.Bootstrap.Native/Release/InsiderNativeIl2CppBootstrapFixture.exe",

    [string] $FakeGameAssembly = "artifacts/native-build/Insider.Bootstrap.Native/Release/GameAssembly.dll",

    [string] $PluginAssembly = "tests/Insider.Il2CppSmokePlugin/bin/Release/netstandard2.0/Insider.Il2CppSmokePlugin.dll",

    [string] $OutputDirectory = "artifacts/il2cpp-host-smoke"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $PackageDirectory))
$fixturePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $FixtureExecutable))
$gameAssemblyPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $FakeGameAssembly))
$pluginAssemblyPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $PluginAssembly))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$artifactsPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $outputPath.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "IL2CPP smoke output must remain below '$artifactsRoot'."
}

foreach ($requiredPath in @($packagePath, $fixturePath, $gameAssemblyPath, $pluginAssemblyPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "IL2CPP smoke input not found: '$requiredPath'."
    }
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

$gameDirectory = Join-Path $outputPath "game"
$metadataDirectory = Join-Path $gameDirectory "TestGame_Data/il2cpp_data/Metadata"
New-Item -ItemType Directory -Path $metadataDirectory -Force | Out-Null

$gameExecutable = Join-Path $gameDirectory "TestGame.exe"
Copy-Item -LiteralPath $fixturePath -Destination $gameExecutable
Copy-Item -LiteralPath $gameAssemblyPath -Destination (Join-Path $gameDirectory "GameAssembly.dll")
[System.IO.File]::WriteAllBytes((Join-Path $metadataDirectory "global-metadata.dat"), [byte[]] @(1))

$cliPath = Join-Path $packagePath "insider.dll"
& dotnet $cliPath install $gameExecutable
if ($LASTEXITCODE -ne 0) {
    throw "The packaged CLI could not install Insider into the IL2CPP smoke game."
}

Copy-Item -LiteralPath $pluginAssemblyPath -Destination (Join-Path $gameDirectory "Insider/plugins")

& $gameExecutable
if ($LASTEXITCODE -ne 0) {
    throw "The real CoreCLR IL2CPP bootstrap fixture failed with exit code $LASTEXITCODE."
}

$managedLogPath = Join-Path $gameDirectory "Insider/logs/insider.log"
if (-not (Test-Path -LiteralPath $managedLogPath -PathType Leaf)) {
    throw "The managed IL2CPP bootstrap log was not created."
}

$managedLog = Get-Content -LiteralPath $managedLogPath -Raw
foreach ($expectedText in @(
    "Insider bootstrap started: UnityIl2Cpp",
    "IL2CPP native metadata and detour services are ready.",
    "Plugin scan completed: 1 loaded, 0 failed."
)) {
    if (-not $managedLog.Contains($expectedText)) {
        throw "The managed IL2CPP bootstrap log is missing '$expectedText'."
    }
}

$pluginMarkerPath = Join-Path $gameDirectory "Insider/il2cpp-smoke.txt"
if (-not (Test-Path -LiteralPath $pluginMarkerPath -PathType Leaf)) {
    throw "The IL2CPP smoke plugin did not write its result marker."
}

$pluginMarker = Get-Content -LiteralPath $pluginMarkerPath -Raw
foreach ($expectedText in @("Backend=UnityIl2Cpp", "Hooked=42", "Restored=7")) {
    if (-not $pluginMarker.Contains($expectedText)) {
        throw "The IL2CPP smoke result is missing '$expectedText'."
    }
}

Write-Host "Verified packaged IL2CPP metadata resolution and native detours through the real private CoreCLR runtime."

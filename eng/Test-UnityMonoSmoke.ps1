[CmdletBinding()]
param(
    [string] $UnityEditor = "C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe",

    [string] $CMake = "",

    [string] $Configuration = "Release",

    [int] $EditorTimeoutSeconds = 600,

    [int] $PlayerTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [int] $TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start '$FilePath'."
    }

    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "'$FilePath' exceeded the $TimeoutSeconds second timeout."
        }

        if ($process.ExitCode -ne 0) {
            throw "'$FilePath' exited with code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Require-Text {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Expected
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected smoke-test output not found: '$Path'."
    }

    $content = [System.IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Expected, [System.StringComparison]::Ordinal)) {
        throw "'$Path' does not contain expected text '$Expected'."
    }
}

function Reject-Text {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Rejected
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected smoke-test output not found: '$Path'."
    }

    $content = [System.IO.File]::ReadAllText($Path)
    if ($content.Contains($Rejected, [System.StringComparison]::Ordinal)) {
        throw "'$Path' contains rejected text '$Rejected'."
    }
}

function Resolve-CMakePath {
    param(
        [string] $RequestedPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = [System.IO.Path]::GetFullPath($RequestedPath)
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            return $resolved
        }

        throw "CMake not found: '$resolved'."
    }

    $command = Get-Command "cmake.exe" -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = "C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $bundled = & $vswhere `
            -latest `
            -products * `
            -requires Microsoft.VisualStudio.Component.VC.CMake.Project `
            -find "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe" |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($bundled)) {
            return [System.IO.Path]::GetFullPath($bundled)
        }
    }

    throw "CMake was not found in PATH or the latest Visual Studio installation."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$smokeRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "unity-mono-smoke"))
$artifactsPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $smokeRoot.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Smoke output must remain below '$artifactsRoot'."
}

$unityPath = [System.IO.Path]::GetFullPath($UnityEditor)
if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf)) {
    throw "Unity Editor not found: '$unityPath'."
}
$cmakePath = Resolve-CMakePath $CMake

if (Test-Path -LiteralPath $smokeRoot) {
    Remove-Item -LiteralPath $smokeRoot -Recurse -Force
}

$nativeBuild = Join-Path $smokeRoot "native"
$cliPublish = Join-Path $smokeRoot "cli"
$packageDirectory = Join-Path $smokeRoot "package"
$playerDirectory = Join-Path $smokeRoot "player"
$playerExecutable = Join-Path $playerDirectory "InsiderUnityMonoSmoke.exe"
$editorLog = Join-Path $smokeRoot "unity-editor.log"
$playerLog = Join-Path $smokeRoot "unity-player.log"
$unityProject = Join-Path $repositoryRoot "tests/UnityMonoSmoke"
$solution = Join-Path $repositoryRoot "Insider.slnx"
$cliProject = Join-Path $repositoryRoot "src/Insider.Cli/Insider.Cli.csproj"
$smokePlugin = Join-Path $repositoryRoot "tests/Insider.UnityMonoSmokePlugin/bin/$Configuration/netstandard2.0/Insider.UnityMonoSmokePlugin.dll"

New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

Invoke-CheckedProcess "dotnet" @(
    "build", $solution,
    "--configuration", $Configuration,
    "--nologo"
) 300

Invoke-CheckedProcess "dotnet" @(
    "publish", $cliProject,
    "--configuration", $Configuration,
    "--output", $cliPublish,
    "--nologo"
) 300

Invoke-CheckedProcess $cmakePath @(
    "-S", (Join-Path $repositoryRoot "native"),
    "-B", $nativeBuild,
    "-A", "x64",
    "-DBUILD_TESTING=OFF"
) 300

Invoke-CheckedProcess $cmakePath @(
    "--build", $nativeBuild,
    "--config", $Configuration
) 300

$nativeBootstrap = Join-Path $nativeBuild "Insider.Bootstrap.Native/$Configuration/version.dll"
$relativeNative = [System.IO.Path]::GetRelativePath($repositoryRoot, $nativeBootstrap)
$relativeCli = [System.IO.Path]::GetRelativePath($repositoryRoot, $cliPublish)
$relativePackage = [System.IO.Path]::GetRelativePath($repositoryRoot, $packageDirectory)
& (Join-Path $repositoryRoot "eng/Build-WindowsPackage.ps1") `
    -NativeBootstrap $relativeNative `
    -CliPublishDirectory $relativeCli `
    -Configuration $Configuration `
    -OutputDirectory $relativePackage
& (Join-Path $repositoryRoot "eng/Test-WindowsPackage.ps1") `
    -PackageDirectory $relativePackage

Invoke-CheckedProcess $unityPath @(
    "-batchmode",
    "-quit",
    "-projectPath", $unityProject,
    "-executeMethod", "Insider.UnityMonoSmoke.Editor.SmokeBuild.BuildWindows64",
    "-insiderSmokeOutput", $playerExecutable,
    "-logFile", $editorLog
) $EditorTimeoutSeconds

$packagedCli = Join-Path $packageDirectory "insider.dll"
Invoke-CheckedProcess "dotnet" @($packagedCli, "inspect", $playerExecutable) 60
Invoke-CheckedProcess "dotnet" @($packagedCli, "install", $playerExecutable) 60

$pluginDirectory = Join-Path $playerDirectory "Insider/plugins"
Copy-Item -LiteralPath $smokePlugin -Destination $pluginDirectory

Invoke-CheckedProcess $playerExecutable @(
    "-batchmode",
    "-nographics",
    "-logFile", $playerLog
) $PlayerTimeoutSeconds

$insiderDirectory = Join-Path $playerDirectory "Insider"
Require-Text (Join-Path $insiderDirectory "logs/native.log") "Managed bootstrap started successfully."
$managedLog = Join-Path $insiderDirectory "logs/insider.log"
Require-Text $managedLog "INSIDER_UNITY_MONO_SMOKE_PLUGIN_LOADED"
Require-Text $managedLog "INSIDER_UNITY_MONO_SMOKE_GAME_HOOKS_REMOVED"
Reject-Text $managedLog "[Error]"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "Backend=UnityMono"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "HookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "RefOutValue=8"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "RefOutOutput=26"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "InParameterHookedValue=14"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "RefReturnHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "RefReturnOriginalValue=12"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "RefReturnReplacementValue=50"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "InstanceHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "VirtualBaseHookedValue=14"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "VirtualOverrideHookedValue=30"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "ValueTypeHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-loaded.txt") "ValueTypeState=7"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooked.txt") "GameHookAssembly=Assembly-CSharp"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooked.txt") "GameHookCount=2"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooked.txt") "GameHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooks-removed.txt") "GameHookAssembly=Assembly-CSharp"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooks-removed.txt") "GameHookCount=0"
Require-Text (Join-Path $insiderDirectory "unity-smoke-game-hooks-removed.txt") "GameRestoredValue=7"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "HookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "RefOutValue=8"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "RefOutOutput=26"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "InParameterHookedValue=14"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "RefReturnHookedValue=50"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "RefReturnOriginalValue=17"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "RefReturnReplacementValue=50"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "InstanceHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "VirtualBaseHookedValue=14"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "VirtualOverrideHookedValue=30"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "ValueTypeHookedValue=42"
Require-Text (Join-Path $insiderDirectory "unity-smoke-plugin-unloaded.txt") "ValueTypeState=7"
Require-Text $playerLog "INSIDER_UNITY_MONO_SMOKE_PLAYER_STARTED"
Require-Text $playerLog "INSIDER_UNITY_MONO_SMOKE_GAME_HOOKED_VALUE=42"
Require-Text $playerLog "INSIDER_UNITY_MONO_SMOKE_GAME_RESTORED_VALUE=7"

Invoke-CheckedProcess "dotnet" @($packagedCli, "status", $playerExecutable) 60

Write-Host "Unity Mono smoke test passed."
Write-Host "Player: $playerExecutable"
Write-Host "Native log: $(Join-Path $insiderDirectory 'logs/native.log')"
Write-Host "Managed log: $(Join-Path $insiderDirectory 'logs/insider.log')"

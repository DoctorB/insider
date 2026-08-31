using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Insider.Bootstrap;
using Insider.Installation;
using Insider.Loader;

namespace Insider.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("loads a valid plugin", LoadsValidPlugin),
            ("rejects duplicate plugin ids", RejectsDuplicateIds),
            ("rejects missing metadata", RejectsMissingMetadata),
            ("contains plugin load failures", ContainsLoadFailure),
            ("unloads plugins in reverse order", UnloadsInReverseOrder),
            ("loads required plugin dependencies first", LoadsRequiredPluginDependenciesFirst),
            ("loads present optional plugin dependencies first", LoadsPresentOptionalPluginDependenciesFirst),
            ("rejects invalid plugin version metadata", RejectsInvalidPluginVersionMetadata),
            ("rejects plugin dependencies below the minimum version", RejectsPluginDependenciesBelowMinimumVersion),
            ("allows optional dependencies below the minimum version", AllowsOptionalDependenciesBelowMinimumVersion),
            ("rejects missing required plugin dependencies", RejectsMissingRequiredPluginDependencies),
            ("rejects required plugin dependency cycles", RejectsRequiredPluginDependencyCycles),
            ("contains failures across required plugin dependencies", ContainsRequiredPluginDependencyFailures),
            ("allows missing optional plugin dependencies", AllowsMissingOptionalPluginDependencies),
            ("fails closed on a missing plugin dependency", FailsClosedOnMissingPluginDependency),
            ("bootstraps a plugin directory end to end", BootstrapsPluginDirectoryEndToEnd),
            ("rejects conflicting plugin dependency versions", RejectsConflictingPluginDependencyVersions),
            ("fails closed on an unsupported managed runtime", FailsClosedOnUnsupportedManagedRuntime),
            ("installs and uninstalls without losing an existing proxy", InstallsAndRestoresExistingProxy),
            ("refuses to remove modified installation files", RefusesToRemoveModifiedFiles),
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                ResetFixtures();
                test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void LoadsValidPlugin()
    {
        var host = CreateHost();
        var result = host.Load(typeof(ValidPlugin));

        Assert(result.Succeeded, result.Error ?? "Plugin did not load.");
        Assert(ValidPlugin.LoadCount == 1, "Load was not called exactly once.");
        Assert(host.LoadedPlugins.Count == 1, "Loaded plugin was not registered.");
    }

    private static void RejectsDuplicateIds()
    {
        var host = CreateHost();
        Assert(host.Load(typeof(ValidPlugin)).Succeeded, "First plugin did not load.");

        var duplicate = host.Load(typeof(DuplicatePlugin));
        Assert(!duplicate.Succeeded, "Duplicate plugin id was accepted.");
        Assert(host.LoadedPlugins.Count == 1, "Duplicate plugin changed the registry.");
    }

    private static void RejectsMissingMetadata()
    {
        var result = CreateHost().Load(typeof(MissingMetadataPlugin));
        Assert(!result.Succeeded, "Plugin without metadata was accepted.");
    }

    private static void ContainsLoadFailure()
    {
        var result = CreateHost().Load(typeof(FailingPlugin));
        Assert(!result.Succeeded, "Failing plugin was reported as loaded.");
        Assert(FailingPlugin.UnloadCount == 1, "Partially loaded plugin was not cleaned up.");
    }

    private static void UnloadsInReverseOrder()
    {
        var host = CreateHost();
        Assert(host.Load(typeof(OrderedPluginA)).Succeeded, "Plugin A did not load.");
        Assert(host.Load(typeof(OrderedPluginB)).Succeeded, "Plugin B did not load.");

        host.UnloadAll();

        Assert(LifecycleEvents.Count == 2, "Unexpected number of unload events.");
        Assert(LifecycleEvents[0] == "B" && LifecycleEvents[1] == "A", "Plugins were not unloaded in reverse order.");
        Assert(host.LoadedPlugins.Count == 0, "Registry was not cleared after unload.");
    }

    private static void LoadsRequiredPluginDependenciesFirst()
    {
        var host = CreateHost();
        var results = host.Load(new[] { typeof(DependentPlugin), typeof(FoundationPlugin) });

        Assert(results.Count == 2 && results[0].Succeeded && results[1].Succeeded, "Plugin dependency graph did not load.");
        Assert(PluginGraphEvents.Count == 2, "Unexpected plugin graph event count.");
        Assert(
            PluginGraphEvents[0] == "foundation" && PluginGraphEvents[1] == "dependent",
            "Required plugin dependency was not loaded first.");

        var dependent = host.LoadedPlugins.First(plugin => plugin.Id == "dev.insider.tests.dependent");
        Assert(dependent.Dependencies.Count == 1, "Plugin descriptor did not expose its dependency.");
        Assert(!dependent.Dependencies[0].Optional, "Required dependency was marked optional.");
        Assert(dependent.Dependencies[0].MinimumVersion == "1.0.0", "Minimum plugin dependency version was not exposed.");
    }

    private static void RejectsMissingRequiredPluginDependencies()
    {
        var result = CreateHost().Load(typeof(MissingRequiredDependencyPlugin));

        Assert(!result.Succeeded, "Plugin with a missing required dependency was loaded.");
        Assert(MissingRequiredDependencyPlugin.LoadCount == 0, "Plugin with a missing dependency executed Load().");
    }

    private static void RejectsInvalidPluginVersionMetadata()
    {
        var host = CreateHost();
        var invalidPlugin = host.Load(typeof(InvalidVersionPlugin));
        var invalidMinimum = host.Load(typeof(InvalidMinimumVersionPlugin));

        Assert(!invalidPlugin.Succeeded, "Plugin with an invalid version was loaded.");
        Assert(!invalidMinimum.Succeeded, "Plugin with an invalid minimum version was loaded.");
        Assert(InvalidVersionPlugin.LoadCount == 0, "Plugin with an invalid version executed Load().");
        Assert(InvalidMinimumVersionPlugin.LoadCount == 0, "Plugin with an invalid minimum version executed Load().");
    }

    private static void RejectsPluginDependenciesBelowMinimumVersion()
    {
        var host = CreateHost();
        var results = host.Load(new[] { typeof(RequiresNewerFoundationPlugin), typeof(FoundationPlugin) });

        Assert(results.Count == 2, "Unexpected result count for minimum-version graph.");
        Assert(results.Count(result => result.Succeeded) == 1, "Minimum-version mismatch did not isolate the dependent plugin.");
        Assert(host.LoadedPlugins.Count == 1 && host.LoadedPlugins.First().Id == "dev.insider.tests.foundation", "Compatible provider was not loaded.");
        Assert(RequiresNewerFoundationPlugin.LoadCount == 0, "Plugin ran with a provider below its minimum version.");
        Assert(
            results.Any(result => result.Error?.Contains("foundation >= 2.0.0 (found 1.0.0)", StringComparison.Ordinal) == true),
            "Minimum-version mismatch was not diagnosed.");
    }

    private static void AllowsOptionalDependenciesBelowMinimumVersion()
    {
        var host = CreateHost();
        Assert(host.Load(typeof(FoundationPlugin)).Succeeded, "Foundation plugin did not load.");

        var result = host.Load(typeof(OptionalNewerFoundationPlugin));

        Assert(result.Succeeded, result.Error ?? "Plugin was blocked by an optional provider below its minimum version.");
        Assert(OptionalNewerFoundationPlugin.LoadCount == 1, "Plugin with an incompatible optional provider did not execute Load().");
    }

    private static void LoadsPresentOptionalPluginDependenciesFirst()
    {
        var host = CreateHost();
        var results = host.Load(new[] { typeof(OptionalDependencyPlugin), typeof(FoundationPlugin) });

        Assert(results.Count == 2 && results[0].Succeeded && results[1].Succeeded, "Present optional dependency graph did not load.");
        Assert(PluginGraphEvents.Count == 2, "Unexpected optional plugin graph event count.");
        Assert(
            PluginGraphEvents[0] == "foundation" && PluginGraphEvents[1] == "optional",
            "Present optional plugin dependency was not loaded first.");
    }

    private static void RejectsRequiredPluginDependencyCycles()
    {
        var host = CreateHost();
        var results = host.Load(new[] { typeof(CyclicPluginA), typeof(CyclicPluginB) });

        Assert(results.Count == 2 && !results[0].Succeeded && !results[1].Succeeded, "Dependency cycle was not rejected.");
        Assert(host.LoadedPlugins.Count == 0, "Plugins from a dependency cycle were loaded.");
    }

    private static void ContainsRequiredPluginDependencyFailures()
    {
        var host = CreateHost();
        var results = host.Load(new[] { typeof(FailingPlugin), typeof(FailingDependentPlugin) });

        Assert(results.Count == 2 && !results[0].Succeeded && !results[1].Succeeded, "Required plugin failure did not propagate.");
        Assert(FailingDependentPlugin.LoadCount == 0, "Dependent plugin executed after its requirement failed.");
    }

    private static void AllowsMissingOptionalPluginDependencies()
    {
        var host = CreateHost();
        var result = host.Load(typeof(OptionalDependencyPlugin));

        Assert(result.Succeeded, result.Error ?? "Plugin with an absent optional dependency did not load.");
        Assert(OptionalDependencyPlugin.LoadCount == 1, "Plugin with an absent optional dependency did not execute Load().");
        Assert(result.Plugin?.Dependencies.Count == 1 && result.Plugin.Dependencies[0].Optional, "Optional dependency metadata was not exposed.");
    }

    private static void BootstrapsPluginDirectoryEndToEnd()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: true);
        fixture.InstallPluginFixture();

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(result.IsSupported, "Unity Mono fixture was not recognized as supported.");
        Assert(result.Runtime.Backend == InsiderRuntimeBackend.UnityMono, "Unexpected runtime backend.");
        Assert(result.LoadedPluginCount == 1, "Fixture plugin was not loaded exactly once.");
        Assert(result.FailedPluginCount == 0, "Fixture plugin load unexpectedly failed.");
        Assert(Directory.Exists(result.PluginDirectory), "Plugin directory was not created.");

        var loadedMarker = Path.Combine(result.InsiderDirectory, "fixture-loaded.txt");
        Assert(File.Exists(loadedMarker), "Fixture plugin did not write its load marker.");
        Assert(File.ReadAllText(loadedMarker).Contains("Backend=UnityMono", StringComparison.Ordinal), "Fixture received the wrong runtime context.");
        Assert(File.ReadAllText(loadedMarker).Contains("Dependency=dependency-v1", StringComparison.Ordinal), "Fixture dependency was not resolved.");

        var log = File.ReadAllText(result.LogPath);
        Assert(log.Contains("Plugin scan completed: 1 loaded, 0 failed.", StringComparison.Ordinal), "Bootstrap summary was not logged.");

        session.Stop();

        Assert(File.Exists(Path.Combine(result.InsiderDirectory, "fixture-unloaded.txt")), "Fixture plugin was not unloaded.");
        Assert(File.ReadAllText(result.LogPath).Contains("Insider bootstrap stopped.", StringComparison.Ordinal), "Bootstrap shutdown was not logged.");

        session.Stop();
    }

    private static void FailsClosedOnMissingPluginDependency()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: true);
        fixture.InstallPluginFixture(includeDependency: false);

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(result.LoadedPluginCount == 0 && result.FailedPluginCount == 1, "Missing dependency did not fail exactly one plugin.");
        Assert(!File.Exists(Path.Combine(result.InsiderDirectory, "fixture-loaded.txt")), "Plugin completed with a missing dependency.");
        Assert(File.ReadAllText(result.LogPath).Contains("is not present under", StringComparison.Ordinal), "Missing dependency was not diagnosed.");
    }

    private static void RejectsConflictingPluginDependencyVersions()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: true);
        fixture.InstallPluginFixture(includeDependency: true);
        fixture.InstallConflictingDependency();

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(result.LoadedPluginCount == 0 && result.FailedPluginCount == 1, "Conflicting dependencies did not stop the plugin scan.");
        Assert(!File.Exists(Path.Combine(result.InsiderDirectory, "fixture-loaded.txt")), "Plugin ran despite conflicting dependencies.");
        Assert(
            File.ReadAllText(result.LogPath).Contains("conflicting assemblies for 'Insider.DependencyFixture'", StringComparison.Ordinal),
            "Dependency conflict was not diagnosed.");
    }

    private static void FailsClosedOnUnsupportedManagedRuntime()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: false);
        using var environment = EnvironmentVariableScope.Clear("DOORSTOP_MONO_LIB_PATH");
        fixture.InstallPluginFixture();

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(!result.IsSupported, "Unknown runtime was incorrectly accepted.");
        Assert(result.Runtime.Backend == InsiderRuntimeBackend.Unknown, "Unexpected runtime backend.");
        Assert(result.LoadedPluginCount == 0 && result.FailedPluginCount == 0, "Plugins were scanned on an unsupported runtime.");
        Assert(!File.Exists(Path.Combine(result.InsiderDirectory, "fixture-loaded.txt")), "Plugin ran on an unsupported runtime.");
        Assert(File.ReadAllText(result.LogPath).Contains("is not supported by this build", StringComparison.Ordinal), "Unsupported runtime was not logged.");
    }

    private static void InstallsAndRestoresExistingProxy()
    {
        using var fixture = InstallationFixture.Create(withExistingProxy: true);
        var installer = new InsiderInstaller();

        var installed = installer.Install(fixture.GameExecutable, fixture.BundleDirectory);

        Assert(installed.State == InsiderInstallationState.Installed, "Installation did not report success.");
        Assert(File.ReadAllText(Path.Combine(fixture.GameDirectory, "version.dll")) == "insider-native", "Native proxy was not installed.");
        Assert(Directory.Exists(Path.Combine(fixture.GameDirectory, "Insider", "plugins")), "Plugin directory was not created.");

        var removed = installer.Uninstall(fixture.GameExecutable);

        Assert(removed.State == InsiderInstallationState.NotInstalled, "Uninstall did not complete.");
        Assert(File.ReadAllText(Path.Combine(fixture.GameDirectory, "version.dll")) == "original-proxy", "Original proxy was not restored.");
        Assert(!File.Exists(Path.Combine(fixture.GameDirectory, "Insider", "install.json")), "Manifest was not removed.");
    }

    private static void RefusesToRemoveModifiedFiles()
    {
        using var fixture = InstallationFixture.Create(withExistingProxy: false);
        var installer = new InsiderInstaller();
        installer.Install(fixture.GameExecutable, fixture.BundleDirectory);

        var modifiedPath = Path.Combine(fixture.GameDirectory, "Insider", "core", "Insider.Loader.dll");
        File.WriteAllText(modifiedPath, "modified-by-user");

        AssertThrows<InsiderInstallationException>(() => installer.Uninstall(fixture.GameExecutable));
        Assert(File.Exists(modifiedPath), "Modified file was removed without --force.");
        Assert(
            installer.GetStatus(fixture.GameExecutable).State == InsiderInstallationState.Damaged,
            "Modified installation was not reported as damaged.");

        installer.Uninstall(fixture.GameExecutable, force: true);
        Assert(!File.Exists(modifiedPath), "Forced uninstall did not remove the modified file.");
    }

    private static PluginHost CreateHost()
    {
        return new PluginHost(new TestContext());
    }

    private static void ResetFixtures()
    {
        ValidPlugin.LoadCount = 0;
        FailingPlugin.UnloadCount = 0;
        MissingRequiredDependencyPlugin.LoadCount = 0;
        FailingDependentPlugin.LoadCount = 0;
        OptionalDependencyPlugin.LoadCount = 0;
        InvalidVersionPlugin.LoadCount = 0;
        InvalidMinimumVersionPlugin.LoadCount = 0;
        RequiresNewerFoundationPlugin.LoadCount = 0;
        OptionalNewerFoundationPlugin.LoadCount = 0;
        LifecycleEvents.Clear();
        PluginGraphEvents.Clear();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    public static List<string> LifecycleEvents { get; } = new List<string>();

    public static List<string> PluginGraphEvents { get; } = new List<string>();
}

internal sealed class BootstrapFixtureWorkspace : IDisposable
{
    private BootstrapFixtureWorkspace(string rootDirectory, string gameDirectory, string pluginDirectory, string dependencyDirectory)
    {
        RootDirectory = rootDirectory;
        GameDirectory = gameDirectory;
        PluginDirectory = pluginDirectory;
        DependencyDirectory = dependencyDirectory;
    }

    public string RootDirectory { get; }

    public string GameDirectory { get; }

    public string PluginDirectory { get; }

    public string DependencyDirectory { get; }

    public static BootstrapFixtureWorkspace Create(bool withMonoRuntime)
    {
        var root = Path.Combine(Path.GetTempPath(), "insider-bootstrap-tests", Guid.NewGuid().ToString("N"));
        var gameDirectory = Path.Combine(root, "game");
        var pluginDirectory = Path.Combine(gameDirectory, "Insider", "plugins");
        var dependencyDirectory = Path.Combine(pluginDirectory, "dependencies");

        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(dependencyDirectory);
        if (withMonoRuntime)
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, "MonoBleedingEdge"));
        }

        return new BootstrapFixtureWorkspace(root, gameDirectory, pluginDirectory, dependencyDirectory);
    }

    public void InstallPluginFixture(bool includeDependency = true)
    {
        File.Copy(
            GetFixturePath("bootstrap", "Insider.PluginFixture.dll"),
            Path.Combine(PluginDirectory, "Insider.PluginFixture.dll"));

        if (includeDependency)
        {
            File.Copy(
                GetFixturePath("dependencies", "v1", "Insider.DependencyFixture.dll"),
                Path.Combine(DependencyDirectory, "Insider.DependencyFixture.dll"));
        }
    }

    public void InstallConflictingDependency()
    {
        var conflictDirectory = Path.Combine(DependencyDirectory, "conflict-v2");
        Directory.CreateDirectory(conflictDirectory);
        File.Copy(
            GetFixturePath("dependencies", "v2", "Insider.DependencyFixture.dll"),
            Path.Combine(conflictDirectory, "Insider.DependencyFixture.dll"));
    }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }

    private static string GetFixturePath(params string[] parts)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }
}

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    private EnvironmentVariableScope(string name)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    public static EnvironmentVariableScope Clear(string name)
    {
        return new EnvironmentVariableScope(name);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}

internal sealed class InstallationFixture : IDisposable
{
    private InstallationFixture(string rootDirectory, string gameDirectory, string gameExecutable, string bundleDirectory)
    {
        RootDirectory = rootDirectory;
        GameDirectory = gameDirectory;
        GameExecutable = gameExecutable;
        BundleDirectory = bundleDirectory;
    }

    public string RootDirectory { get; }

    public string GameDirectory { get; }

    public string GameExecutable { get; }

    public string BundleDirectory { get; }

    public static InstallationFixture Create(bool withExistingProxy)
    {
        var root = Path.Combine(Path.GetTempPath(), "insider-tests", Guid.NewGuid().ToString("N"));
        var gameDirectory = Path.Combine(root, "game");
        var bundleDirectory = Path.Combine(root, "bundle");
        var gameExecutable = Path.Combine(gameDirectory, "TestGame.exe");

        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(Path.Combine(bundleDirectory, "native", "win-x64"));
        Directory.CreateDirectory(Path.Combine(bundleDirectory, "core"));
        File.WriteAllText(gameExecutable, "test-game");
        File.WriteAllText(Path.Combine(bundleDirectory, "native", "win-x64", "version.dll"), "insider-native");
        File.WriteAllText(Path.Combine(bundleDirectory, "core", "Insider.Abstractions.dll"), "abstractions");
        File.WriteAllText(Path.Combine(bundleDirectory, "core", "Insider.Loader.dll"), "loader");
        File.WriteAllText(Path.Combine(bundleDirectory, "core", "Insider.Bootstrap.dll"), "bootstrap");

        if (withExistingProxy)
        {
            File.WriteAllText(Path.Combine(gameDirectory, "version.dll"), "original-proxy");
        }

        return new InstallationFixture(root, gameDirectory, gameExecutable, bundleDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}

[InsiderPlugin("dev.insider.tests.valid", "Valid", "1.0.0")]
public sealed class ValidPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.valid", "Duplicate", "1.0.0")]
public sealed class DuplicatePlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
    }
}

public sealed class MissingMetadataPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing", "Failing", "1.0.0")]
public sealed class FailingPlugin : IInsiderPlugin
{
    public static int UnloadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        throw new InvalidOperationException("Expected test failure.");
    }

    public void Unload()
    {
        UnloadCount++;
    }
}

[InsiderPlugin("dev.insider.tests.a", "A", "1.0.0")]
public sealed class OrderedPluginA : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
        Program.LifecycleEvents.Add("A");
    }
}

[InsiderPlugin("dev.insider.tests.b", "B", "1.0.0")]
public sealed class OrderedPluginB : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
        Program.LifecycleEvents.Add("B");
    }
}

[InsiderPlugin("dev.insider.tests.foundation", "Foundation", "1.0.0")]
public sealed class FoundationPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        Program.PluginGraphEvents.Add("foundation");
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.dependent", "Dependent", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.foundation", "1.0.0")]
public sealed class DependentPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        Program.PluginGraphEvents.Add("dependent");
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.invalid-version", "Invalid Version", "1.0")]
public sealed class InvalidVersionPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.invalid-minimum", "Invalid Minimum", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.foundation", "1.0")]
public sealed class InvalidMinimumVersionPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.requires-newer", "Requires Newer", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.foundation", "2.0.0")]
public sealed class RequiresNewerFoundationPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.optional-newer", "Optional Newer", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.foundation", "2.0.0", optional: true)]
public sealed class OptionalNewerFoundationPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.missing-required", "Missing Required", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.not-installed")]
public sealed class MissingRequiredDependencyPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.cycle-a", "Cycle A", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.cycle-b")]
public sealed class CyclicPluginA : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.cycle-b", "Cycle B", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.cycle-a")]
public sealed class CyclicPluginB : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing-dependent", "Failing Dependent", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.failing")]
public sealed class FailingDependentPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.optional", "Optional", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.foundation", "1.0.0", optional: true)]
public sealed class OptionalDependencyPlugin : IInsiderPlugin
{
    public static int LoadCount { get; set; }

    public void Load(IInsiderContext context)
    {
        LoadCount++;
        Program.PluginGraphEvents.Add("optional");
    }

    public void Unload()
    {
    }
}

internal sealed class TestContext : IInsiderContext
{
    public string GameDirectory { get; } = "/game";

    public string InsiderDirectory { get; } = "/game/Insider";

    public IInsiderLogger Logger { get; } = new TestLogger();

    public IInsiderRuntimeInfo Runtime { get; } = new TestRuntimeInfo();
}

internal sealed class TestLogger : IInsiderLogger
{
    public void Log(InsiderLogLevel level, string message, Exception? exception = null)
    {
    }
}

internal sealed class TestRuntimeInfo : IInsiderRuntimeInfo
{
    public InsiderRuntimeBackend Backend { get; } = InsiderRuntimeBackend.UnityMono;

    public string OperatingSystem { get; } = "Test";

    public string Architecture { get; } = "x64";

    public string RuntimeVersion { get; } = "Test";
}

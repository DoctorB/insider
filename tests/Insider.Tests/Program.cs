using System;
using System.Collections.Generic;
using System.IO;
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
        LifecycleEvents.Clear();
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

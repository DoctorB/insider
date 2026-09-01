using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Insider.Bootstrap;
using Insider.Hooking;
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
            ("scopes plugin log messages by id", ScopesPluginLogMessagesById),
            ("applies and removes a managed detour", AppliesAndRemovesManagedDetour),
            ("detours a method with ref and out parameters", DetoursMethodWithRefAndOutParameters),
            ("detours an instance method and calls the original", DetoursInstanceMethodAndCallsOriginal),
            ("detours virtual base and override implementations independently", DetoursVirtualImplementationsIndependently),
            ("detours a value-type instance method with ref self", DetoursValueTypeInstanceMethodWithRefSelf),
            ("detours an instance constructor and calls the original", DetoursInstanceConstructorAndCallsOriginal),
            ("rejects incompatible managed detour signatures", RejectsIncompatibleManagedDetourSignatures),
            ("chains and selectively removes managed detours", ChainsAndSelectivelyRemovesManagedDetours),
            ("removes plugin detours during unload", RemovesPluginDetoursDuringUnload),
            ("removes plugin detours after failed load", RemovesPluginDetoursAfterFailedLoad),
            ("preserves other plugin detours after failed load", PreservesOtherPluginDetoursAfterFailedLoad),
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

    private static void ScopesPluginLogMessagesById()
    {
        var context = new TestContext();
        var host = new PluginHost(context);

        var result = host.Load(typeof(LoggingPlugin));

        Assert(result.Succeeded, result.Error ?? "Logging plugin did not load.");
        Assert(
            context.CapturedLogger.Messages.Contains("[dev.insider.tests.logging] hello"),
            "Plugin log message did not include its plugin id.");
        Assert(
            context.CapturedLogger.Messages.Contains("Loaded plugin dev.insider.tests.logging 1.0.0."),
            "Loader message was unexpectedly changed by the plugin scope.");
    }

    private static void AppliesAndRemovesManagedDetour()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        Assert(ManagedHookTarget.Value() == 7, "Managed hook target did not begin with its original value.");
        using (service.Detour(target, (Func<int>)ManagedHookTarget.Replacement))
        {
            Assert(ManagedHookTarget.Value() == 42, "Managed detour did not replace the target method.");
        }

        Assert(ManagedHookTarget.Value() == 7, "Disposing the managed detour did not restore the target method.");
    }

    private static void DetoursInstanceMethodAndCallsOriginal()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedInstanceHookTarget), nameof(ManagedInstanceHookTarget.Add));
        var instance = new ManagedInstanceHookTarget(3);

        Assert(instance.Add(4) == 7, "Instance hook target did not begin with its original value.");
        using (service.Detour(target, (ManagedInstanceReplacement)ManagedInstanceHookTarget.Replacement))
        {
            Assert(instance.Add(4) == 14, "Instance detour did not receive self or call the original method.");
        }

        Assert(instance.Add(4) == 7, "Disposing the instance detour did not restore the target method.");
    }

    private static void DetoursMethodWithRefAndOutParameters()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedRefOutHookTarget), nameof(ManagedRefOutHookTarget.TryTransform));
        var value = 2;

        Assert(ManagedRefOutHookTarget.TryTransform(ref value, out var output), "Ref/out hook target returned false.");
        Assert(value == 7 && output == 14, "Ref/out hook target did not begin with its original values.");

        value = 2;
        using (service.Detour(target, (ManagedRefOutReplacement)ManagedRefOutHookTarget.Replacement))
        {
            Assert(ManagedRefOutHookTarget.TryTransform(ref value, out output), "Ref/out detour returned false.");
            Assert(value == 8, "Ref/out detour did not preserve the ref mutation.");
            Assert(output == 26, "Ref/out detour did not preserve and extend the out value.");
        }

        value = 2;
        Assert(ManagedRefOutHookTarget.TryTransform(ref value, out output), "Restored ref/out hook target returned false.");
        Assert(value == 7 && output == 14, "Disposing the ref/out detour did not restore the target method.");
    }

    private static void DetoursVirtualImplementationsIndependently()
    {
        var service = new RuntimeDetourHookService();
        var baseTarget = GetRequiredMethod(
            typeof(ManagedVirtualBaseHookTarget),
            nameof(ManagedVirtualBaseHookTarget.Calculate));
        var overrideTarget = GetRequiredMethod(
            typeof(ManagedVirtualDerivedHookTarget),
            nameof(ManagedVirtualDerivedHookTarget.Calculate));
        var baseInstance = new ManagedVirtualBaseHookTarget();
        ManagedVirtualBaseHookTarget derivedInstance = new ManagedVirtualDerivedHookTarget();

        Assert(baseInstance.Calculate(2) == 7, "Virtual base target did not begin with its original value.");
        Assert(derivedInstance.Calculate(2) == 10, "Virtual override target did not begin with its original value.");

        using (service.Detour(
            baseTarget,
            (ManagedVirtualBaseReplacement)ManagedVirtualBaseHookTarget.Replacement))
        {
            Assert(baseInstance.Calculate(2) == 14, "Virtual base detour did not call its original implementation.");
            Assert(derivedInstance.Calculate(2) == 10, "Virtual base detour unexpectedly replaced the override.");

            using (service.Detour(
                overrideTarget,
                (ManagedVirtualDerivedReplacement)ManagedVirtualDerivedHookTarget.Replacement))
            {
                Assert(baseInstance.Calculate(2) == 14, "Virtual override detour changed the base implementation.");
                Assert(derivedInstance.Calculate(2) == 30, "Virtual override detour did not call its original implementation.");
            }

            Assert(derivedInstance.Calculate(2) == 10, "Removing the override detour did not restore the override.");
        }

        Assert(baseInstance.Calculate(2) == 7, "Removing the base detour did not restore the base implementation.");
        Assert(derivedInstance.Calculate(2) == 10, "Removing the base detour changed the restored override.");
    }

    private static void DetoursInstanceConstructorAndCallsOriginal()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredConstructor(typeof(ManagedConstructorHookTarget), typeof(int));

        var original = (ManagedConstructorHookTarget)target.Invoke(new object[] { 5 });
        Assert(original.Value == 5, "Constructor hook target did not begin with its original value.");

        using (service.Detour(target, (ManagedConstructorReplacement)ManagedConstructorHookTarget.Replacement))
        {
            var hooked = (ManagedConstructorHookTarget)target.Invoke(new object[] { 5 });
            Assert(hooked.Value == 12, "Constructor detour did not receive self or call the original constructor.");
        }

        var restored = (ManagedConstructorHookTarget)target.Invoke(new object[] { 5 });
        Assert(restored.Value == 5, "Disposing the constructor detour did not restore the target constructor.");
    }

    private static void DetoursValueTypeInstanceMethodWithRefSelf()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedValueHookTarget), nameof(ManagedValueHookTarget.Add));
        var instance = new ManagedValueHookTarget(5);

        Assert(instance.Add(2) == 7, "Value-type hook target did not begin with its original value.");
        instance = new ManagedValueHookTarget(5);

        using (service.Detour(target, (ManagedValueReplacement)ManagedValueHookTarget.Replacement))
        {
            Assert(instance.Add(2) == 19, "Value-type detour did not receive ref self or call the original method.");
            Assert(instance.Value == 9, "Value-type detour did not preserve the original mutation through ref self.");
        }

        instance = new ManagedValueHookTarget(5);
        Assert(instance.Add(2) == 7, "Disposing the value-type detour did not restore the target method.");
    }

    private static void RejectsIncompatibleManagedDetourSignatures()
    {
        var service = new RuntimeDetourHookService();
        var staticTarget = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));
        var instanceTarget = GetRequiredMethod(typeof(ManagedInstanceHookTarget), nameof(ManagedInstanceHookTarget.Add));
        var refOutTarget = GetRequiredMethod(typeof(ManagedRefOutHookTarget), nameof(ManagedRefOutHookTarget.TryTransform));
        var valueTypeTarget = GetRequiredMethod(typeof(ManagedValueHookTarget), nameof(ManagedValueHookTarget.Add));
        var valueTypeConstructorTarget = GetRequiredConstructor(typeof(ManagedValueHookTarget), typeof(int));
        var staticConstructorTarget = typeof(ManagedStaticConstructorHookTarget).TypeInitializer
            ?? throw new InvalidOperationException("Static constructor hook target was not found.");

        AssertThrows<ArgumentException>(
            () => service.Detour(staticTarget, (Func<int, int>)ManagedHookTarget.ReplacementWithArgument));
        AssertThrows<ArgumentException>(
            () => service.Detour(instanceTarget, (Func<int, int>)ManagedInstanceHookTarget.ReplacementWithoutSelf));
        var refOutException = AssertThrows<ArgumentException>(
            () => service.Detour(refOutTarget, (Func<bool>)ManagedRefOutHookTarget.InvalidReplacement));
        Assert(
            refOutException.Message.Contains("byref System.Int32", StringComparison.Ordinal),
            "By-reference signature mismatch did not use the readable diagnostic format.");
        AssertThrows<ArgumentException>(
            () => service.Detour(valueTypeTarget, (Func<ManagedValueHookTarget, int>)ManagedValueHookTarget.InvalidReplacement));
        AssertThrows<NotSupportedException>(
            () => service.Detour(valueTypeConstructorTarget, (Action)ManagedStaticConstructorHookTarget.Replacement));
        AssertThrows<NotSupportedException>(
            () => service.Detour(staticConstructorTarget, (Action)ManagedStaticConstructorHookTarget.Replacement));
    }

    private static void ChainsAndSelectivelyRemovesManagedDetours()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        using (service.Detour(target, (ManagedHookReplacement)ManagedHookTarget.AddTen))
        {
            Assert(ManagedHookTarget.Value() == 17, "The first managed detour did not call the original method.");

            using (service.Detour(target, (ManagedHookReplacement)ManagedHookTarget.AddTwenty))
            {
                Assert(ManagedHookTarget.Value() == 37, "Managed detours did not compose into one chain.");
            }

            Assert(ManagedHookTarget.Value() == 17, "Removing one managed detour broke the remaining chain.");
        }

        Assert(ManagedHookTarget.Value() == 7, "Removing the managed detour chain did not restore the target.");
    }

    private static void RemovesPluginDetoursDuringUnload()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var result = host.Load(typeof(HookingPlugin));

        Assert(result.Succeeded, result.Error ?? "Hooking plugin did not load.");
        Assert(ManagedHookTarget.Value() == 42, "Plugin-owned detour was not applied.");

        host.UnloadAll();

        Assert(ManagedHookTarget.Value() == 7, "Plugin-owned detour remained applied after unload.");
    }

    private static void RemovesPluginDetoursAfterFailedLoad()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var result = host.Load(typeof(FailingHookingPlugin));

        Assert(!result.Succeeded, "Failing hooking plugin was reported as loaded.");
        Assert(ManagedHookTarget.Value() == 7, "Failed plugin load left its detour applied.");
    }

    private static void PreservesOtherPluginDetoursAfterFailedLoad()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var loaded = host.Load(typeof(ChainHookingPlugin));
        Assert(loaded.Succeeded, loaded.Error ?? "Chain hooking plugin did not load.");
        Assert(ManagedHookTarget.Value() == 17, "The existing plugin detour was not applied.");

        var failed = host.Load(typeof(FailingChainHookingPlugin));
        Assert(!failed.Succeeded, "Failing chain hooking plugin was reported as loaded.");
        Assert(ManagedHookTarget.Value() == 17, "Failed plugin cleanup removed or changed another plugin's detour.");

        host.UnloadAll();
        Assert(ManagedHookTarget.Value() == 7, "Unloading the remaining plugin did not restore the target.");
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
        Assert(
            log.Contains("[dev.insider.tests.bootstrap-fixture] Bootstrap fixture loaded.", StringComparison.Ordinal),
            "Plugin-scoped message was not persisted to the bootstrap log.");

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

    private static PluginHost CreateHost(IInsiderHookService? hooks = null)
    {
        return new PluginHost(new TestContext(hooks));
    }

    private static MethodInfo GetRequiredMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Method '{type.FullName}.{name}' was not found.");
    }

    private static ConstructorInfo GetRequiredConstructor(Type type, params Type[] parameterTypes)
    {
        return type.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null)
            ?? throw new InvalidOperationException($"Constructor on '{type.FullName}' was not found.");
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

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
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
        var managedFiles = new[]
        {
            "Insider.Abstractions.dll",
            "Insider.Loader.dll",
            "Insider.Bootstrap.dll",
            "Insider.Hooking.dll",
            "Mono.Cecil.dll",
            "Mono.Cecil.Mdb.dll",
            "Mono.Cecil.Pdb.dll",
            "Mono.Cecil.Rocks.dll",
            "MonoMod.Backports.dll",
            "MonoMod.Core.dll",
            "MonoMod.Iced.dll",
            "MonoMod.ILHelpers.dll",
            "MonoMod.RuntimeDetour.dll",
            "MonoMod.Utils.dll",
            "System.Reflection.Emit.ILGeneration.dll",
            "System.Reflection.Emit.Lightweight.dll",
        };
        foreach (var file in managedFiles)
        {
            File.WriteAllText(Path.Combine(bundleDirectory, "core", file), file);
        }

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

[InsiderPlugin("dev.insider.tests.logging", "Logging", "1.0.0")]
public sealed class LoggingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        context.Logger.Info("hello");
    }

    public void Unload()
    {
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
    public TestContext(IInsiderHookService? hooks = null)
    {
        CapturedLogger = new TestLogger();
        Logger = CapturedLogger;
        Hooks = hooks ?? new NoOpHookService();
    }

    public string GameDirectory { get; } = "/game";

    public string InsiderDirectory { get; } = "/game/Insider";

    public IInsiderLogger Logger { get; }

    public TestLogger CapturedLogger { get; }

    public IInsiderRuntimeInfo Runtime { get; } = new TestRuntimeInfo();

    public IInsiderHookService Hooks { get; }
}

internal sealed class NoOpHookService : IInsiderHookService
{
    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        return new NoOpDetour();
    }

    private sealed class NoOpDetour : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class TestLogger : IInsiderLogger
{
    public List<string> Messages { get; } = new List<string>();

    public void Log(InsiderLogLevel level, string message, Exception? exception = null)
    {
        Messages.Add(message);
    }
}

internal sealed class TestRuntimeInfo : IInsiderRuntimeInfo
{
    public InsiderRuntimeBackend Backend { get; } = InsiderRuntimeBackend.UnityMono;

    public string OperatingSystem { get; } = "Test";

    public string Architecture { get; } = "x64";

    public string RuntimeVersion { get; } = "Test";
}

internal static class ManagedHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value()
    {
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement()
    {
        return 42;
    }

    public static int ReplacementWithArgument(int value)
    {
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AddTen(ManagedHookOriginal original)
    {
        return original() + 10;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AddTwenty(ManagedHookOriginal original)
    {
        return original() + 20;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AddOneHundred(ManagedHookOriginal original)
    {
        return original() + 100;
    }
}

internal delegate int ManagedHookOriginal();

internal delegate int ManagedHookReplacement(ManagedHookOriginal original);

internal delegate bool ManagedRefOutOriginal(ref int value, out int output);

internal delegate bool ManagedRefOutReplacement(
    ManagedRefOutOriginal original,
    ref int value,
    out int output);

internal static class ManagedRefOutHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TryTransform(ref int value, out int output)
    {
        value += 5;
        output = value * 2;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Replacement(
        ManagedRefOutOriginal original,
        ref int value,
        out int output)
    {
        value++;
        var result = original(ref value, out output);
        output += 10;
        return result;
    }

    public static bool InvalidReplacement()
    {
        return false;
    }
}

internal delegate int ManagedInstanceOriginal(ManagedInstanceHookTarget self, int value);

internal delegate int ManagedInstanceReplacement(
    ManagedInstanceOriginal original,
    ManagedInstanceHookTarget self,
    int value);

internal delegate int ManagedVirtualBaseOriginal(ManagedVirtualBaseHookTarget self, int value);

internal delegate int ManagedVirtualBaseReplacement(
    ManagedVirtualBaseOriginal original,
    ManagedVirtualBaseHookTarget self,
    int value);

internal delegate int ManagedVirtualDerivedOriginal(ManagedVirtualDerivedHookTarget self, int value);

internal delegate int ManagedVirtualDerivedReplacement(
    ManagedVirtualDerivedOriginal original,
    ManagedVirtualDerivedHookTarget self,
    int value);

internal class ManagedVirtualBaseHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual int Calculate(int value)
    {
        return value + 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(
        ManagedVirtualBaseOriginal original,
        ManagedVirtualBaseHookTarget self,
        int value)
    {
        return original(self, value) * 2;
    }
}

internal sealed class ManagedVirtualDerivedHookTarget : ManagedVirtualBaseHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override int Calculate(int value)
    {
        return value + 8;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(
        ManagedVirtualDerivedOriginal original,
        ManagedVirtualDerivedHookTarget self,
        int value)
    {
        return original(self, value) + 20;
    }
}

internal delegate void ManagedConstructorOriginal(ManagedConstructorHookTarget self, int value);

internal delegate void ManagedConstructorReplacement(
    ManagedConstructorOriginal original,
    ManagedConstructorHookTarget self,
    int value);

internal sealed class ManagedConstructorHookTarget
{
    public ManagedConstructorHookTarget(int value)
    {
        Value = value;
    }

    public int Value { get; private set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Replacement(
        ManagedConstructorOriginal original,
        ManagedConstructorHookTarget self,
        int value)
    {
        original(self, value + 1);
        self.Value *= 2;
    }
}

internal sealed class ManagedInstanceHookTarget
{
    private readonly int _baseValue;

    public ManagedInstanceHookTarget(int baseValue)
    {
        _baseValue = baseValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Add(int value)
    {
        return _baseValue + value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(
        ManagedInstanceOriginal original,
        ManagedInstanceHookTarget self,
        int value)
    {
        return original(self, value) * 2;
    }

    public static int ReplacementWithoutSelf(int value)
    {
        return value;
    }
}

internal delegate int ManagedValueOriginal(ref ManagedValueHookTarget self, int value);

internal delegate int ManagedValueReplacement(
    ManagedValueOriginal original,
    ref ManagedValueHookTarget self,
    int value);

internal struct ManagedValueHookTarget
{
    private int _value;

    public ManagedValueHookTarget(int value)
    {
        _value = value;
    }

    public int Value => _value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Add(int value)
    {
        _value += value;
        return _value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(
        ManagedValueOriginal original,
        ref ManagedValueHookTarget self,
        int value)
    {
        return original(ref self, value * 2) + 10;
    }

    public static int InvalidReplacement(ManagedValueHookTarget self)
    {
        return self.Value;
    }
}

internal static class ManagedStaticConstructorHookTarget
{
    static ManagedStaticConstructorHookTarget()
    {
    }

    public static void Replacement()
    {
    }
}

[InsiderPlugin("dev.insider.tests.hooking", "Hooking", "1.0.0")]
public sealed class HookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _ = context.Hooks.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing-hooking", "Failing Hooking", "1.0.0")]
public sealed class FailingHookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _ = context.Hooks.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
        throw new InvalidOperationException("Expected failure after applying a detour.");
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.chain-hooking", "Chain Hooking", "1.0.0")]
public sealed class ChainHookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _ = context.Hooks.Detour(target, (ManagedHookReplacement)ManagedHookTarget.AddTen);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing-chain-hooking", "Failing Chain Hooking", "1.0.0")]
public sealed class FailingChainHookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _ = context.Hooks.Detour(target, (ManagedHookReplacement)ManagedHookTarget.AddOneHundred);
        throw new InvalidOperationException("Expected failure after extending a detour chain.");
    }

    public void Unload()
    {
    }
}

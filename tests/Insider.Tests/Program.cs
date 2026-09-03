using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Insider.Bootstrap;
using Insider.Cli;
using Insider.Hooking;
using Insider.Installation;
using Insider.Loader;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using CliProgram = Insider.Cli.Program;
using ReflectionEmit = System.Reflection.Emit;

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
            ("scopes persistent directories by plugin id", ScopesPersistentDirectoriesByPluginId),
            ("contains plugin main-thread callback failures", ContainsPluginMainThreadCallbackFailures),
            ("cancels queued main-thread callbacks during plugin unload", CancelsQueuedMainThreadCallbacksDuringUnload),
            ("removes plugin update callbacks during unload", RemovesPluginUpdateCallbacksDuringUnload),
            ("applies and removes a managed detour", AppliesAndRemovesManagedDetour),
            ("detours a method with ref and out parameters", DetoursMethodWithRefAndOutParameters),
            ("detours a method with in parameters", DetoursMethodWithInParameters),
            ("detours a method with a by-reference return", DetoursMethodWithByReferenceReturn),
            ("rejects generic managed detour targets", RejectsGenericManagedDetourTargets),
            ("detours an instance method and calls the original", DetoursInstanceMethodAndCallsOriginal),
            ("detours virtual base and override implementations independently", DetoursVirtualImplementationsIndependently),
            ("detours a value-type instance method with ref self", DetoursValueTypeInstanceMethodWithRefSelf),
            ("detours an instance constructor and calls the original", DetoursInstanceConstructorAndCallsOriginal),
            ("rejects incompatible managed detour signatures", RejectsIncompatibleManagedDetourSignatures),
            ("rejects multicast managed detour replacements", RejectsMulticastManagedDetourReplacement),
            ("chains and selectively removes managed detours", ChainsAndSelectivelyRemovesManagedDetours),
            ("removes plugin detours during unload", RemovesPluginDetoursDuringUnload),
            ("removes plugin detours after failed load", RemovesPluginDetoursAfterFailedLoad),
            ("preserves other plugin detours after failed load", PreservesOtherPluginDetoursAfterFailedLoad),
            ("retries a failed plugin detour removal", RetriesFailedPluginDetourRemoval),
            ("continues plugin detour cleanup after a removal failure", ContinuesPluginDetourCleanupAfterFailure),
            ("applies and removes an IL hook", AppliesAndRemovesIlHook),
            ("chains and selectively removes IL hooks", ChainsAndSelectivelyRemovesIlHooks),
            ("combines an IL hook with a managed detour", CombinesIlHookWithManagedDetour),
            ("rewrites a value-type constructor IL body", RewritesValueTypeConstructorIl),
            ("rejects unsupported IL hook targets", RejectsUnsupportedIlHookTargets),
            ("rejects multicast IL manipulators", RejectsMulticastIlManipulator),
            ("wraps IL manipulator failures", WrapsIlManipulatorFailures),
            ("removes plugin IL hooks during unload", RemovesPluginIlHooksDuringUnload),
            ("removes plugin IL hooks after failed load", RemovesPluginIlHooksAfterFailedLoad),
            ("preserves other plugin IL hooks after failed load", PreservesOtherPluginIlHooksAfterFailedLoad),
            ("retries a failed plugin IL hook removal", RetriesFailedPluginIlHookRemoval),
            ("continues plugin IL hook cleanup after a removal failure", ContinuesPluginIlHookCleanupAfterFailure),
            ("unloads plugins in reverse order", UnloadsInReverseOrder),
            ("loads required plugin dependencies first", LoadsRequiredPluginDependenciesFirst),
            ("loads present optional plugin dependencies first", LoadsPresentOptionalPluginDependenciesFirst),
            ("handles dependencies on disabled plugins", HandlesDependenciesOnDisabledPlugins),
            ("rejects invalid plugin version metadata", RejectsInvalidPluginVersionMetadata),
            ("rejects plugin dependencies below the minimum version", RejectsPluginDependenciesBelowMinimumVersion),
            ("allows optional dependencies below the minimum version", AllowsOptionalDependenciesBelowMinimumVersion),
            ("rejects missing required plugin dependencies", RejectsMissingRequiredPluginDependencies),
            ("rejects required plugin dependency cycles", RejectsRequiredPluginDependencyCycles),
            ("contains failures across required plugin dependencies", ContainsRequiredPluginDependencyFailures),
            ("allows missing optional plugin dependencies", AllowsMissingOptionalPluginDependencies),
            ("fails closed on a missing plugin dependency", FailsClosedOnMissingPluginDependency),
            ("resolves host dependencies from the Insider core", ResolvesHostDependencyFromCore),
            ("bootstraps a plugin directory end to end", BootstrapsPluginDirectoryEndToEnd),
            ("skips plugins in the bootstrap disable list", SkipsPluginsInBootstrapDisableList),
            ("rejects conflicting plugin dependency versions", RejectsConflictingPluginDependencyVersions),
            ("fails closed on an unsupported managed runtime", FailsClosedOnUnsupportedManagedRuntime),
            ("installs and uninstalls without losing an existing proxy", InstallsAndRestoresExistingProxy),
            ("refuses to remove modified installation files", RefusesToRemoveModifiedFiles),
            ("manages disabled plugins through the CLI", ManagesDisabledPluginsThroughCli),
            ("diagnoses game plugins without activation", DiagnosesGamePluginsWithoutActivation),
            ("dispatches Unity main-thread callbacks", DispatchesUnityMainThreadCallbacks),
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
                Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
            }
        }

        TestContext.ResetDirectories();
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

    private static void ScopesPersistentDirectoriesByPluginId()
    {
        var context = new TestContext();
        using var host = new PluginHost(context);
        var results = host.Load(new[]
        {
            typeof(DirectoryPluginA),
            typeof(DirectoryPluginB),
            typeof(UnsafeDirectoryPlugin),
        });

        Assert(results.All(result => result.Succeeded), "A directory fixture plugin did not load.");
        var first = DirectoryPluginA.Context
            ?? throw new InvalidOperationException("The first plugin did not capture its context.");
        var second = DirectoryPluginB.Context
            ?? throw new InvalidOperationException("The second plugin did not capture its context.");
        var unsafeContext = UnsafeDirectoryPlugin.Context
            ?? throw new InvalidOperationException("The unsafe-ID plugin did not capture its context.");

        var assemblyDirectory = Path.GetDirectoryName(typeof(DirectoryPluginA).Assembly.Location)
            ?? throw new InvalidOperationException("The test assembly has no parent directory.");
        Assert(first.PluginDirectory == assemblyDirectory, "PluginDirectory did not identify the entry assembly directory.");
        Assert(first.ConfigDirectory != second.ConfigDirectory, "Plugins shared a configuration directory.");
        Assert(first.DataDirectory != second.DataDirectory, "Plugins shared a data directory.");
        Assert(Directory.Exists(first.ConfigDirectory), "The configuration directory was not created before Load().");
        Assert(Directory.Exists(first.DataDirectory), "The data directory was not created before Load().");

        var configRoot = Path.GetFullPath(context.ConfigDirectory) + Path.DirectorySeparatorChar;
        var dataRoot = Path.GetFullPath(context.DataDirectory) + Path.DirectorySeparatorChar;
        Assert(
            Path.GetFullPath(unsafeContext.ConfigDirectory).StartsWith(configRoot, StringComparison.OrdinalIgnoreCase),
            "An unsafe plugin ID escaped the configuration root.");
        Assert(
            Path.GetFullPath(unsafeContext.DataDirectory).StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase),
            "An unsafe plugin ID escaped the data root.");

        var configFile = Path.Combine(first.ConfigDirectory, "settings.txt");
        var dataFile = Path.Combine(first.DataDirectory, "state.txt");
        File.WriteAllText(configFile, "configuration");
        File.WriteAllText(dataFile, "state");

        host.UnloadAll();
        Assert(File.Exists(configFile), "Plugin configuration was removed during unload.");
        Assert(File.Exists(dataFile), "Plugin data was removed during unload.");
    }

    private static void ContainsPluginMainThreadCallbackFailures()
    {
        var mainThread = new ManualMainThread();
        var context = new TestContext(mainThread: mainThread);
        using var host = new PluginHost(context);

        var result = host.Load(typeof(MainThreadCallbackPlugin));
        Assert(result.Succeeded, result.Error ?? "Main-thread callback plugin did not load.");

        mainThread.Drain();

        Assert(MainThreadCallbackPlugin.SuccessCount == 1, "A failed callback stopped the remaining plugin callbacks.");
        Assert(
            context.CapturedLogger.Messages.Any(message =>
                message.Contains("[dev.insider.tests.main-thread-callback] Main-thread callback failed.", StringComparison.Ordinal)),
            "The failed callback was not logged with its plugin id.");
    }

    private static void CancelsQueuedMainThreadCallbacksDuringUnload()
    {
        var mainThread = new ManualMainThread();
        using var host = new PluginHost(new TestContext(mainThread: mainThread));

        var result = host.Load(typeof(MainThreadCancellationPlugin));
        Assert(result.Succeeded, result.Error ?? "Main-thread cancellation plugin did not load.");

        host.UnloadAll();
        mainThread.Drain();

        Assert(MainThreadCancellationPlugin.CallbackCount == 0, "A queued callback ran after plugin unload.");
        AssertThrows<ObjectDisposedException>(() =>
            (MainThreadCancellationPlugin.Dispatcher
                ?? throw new InvalidOperationException("Plugin dispatcher was not captured."))
            .Post(() => { }));
    }

    private static void RemovesPluginUpdateCallbacksDuringUnload()
    {
        var mainThread = new ManualMainThread();
        var context = new TestContext(mainThread: mainThread);
        using var host = new PluginHost(context);

        var result = host.Load(typeof(MainThreadUpdatePlugin));
        Assert(result.Succeeded, result.Error ?? "Main-thread update plugin did not load.");
        Assert(mainThread.UpdateCount == 2, "The plugin update callbacks were not registered.");

        mainThread.Drain();
        Assert(MainThreadUpdatePlugin.SuccessCount == 1, "A failed update stopped another plugin update callback.");
        Assert(
            context.CapturedLogger.Messages.Any(message =>
                message.Contains("[dev.insider.tests.main-thread-update] Main-thread update callback failed.", StringComparison.Ordinal)),
            "The failed update callback was not logged with its plugin id.");

        host.UnloadAll();
        Assert(mainThread.UpdateCount == 0, "Plugin update callbacks remained registered after unload.");
        mainThread.Drain();
        Assert(MainThreadUpdatePlugin.SuccessCount == 1, "A plugin update callback ran after unload.");

        MainThreadUpdatePlugin.Handle?.Dispose();
        AssertThrows<ObjectDisposedException>(() =>
            (MainThreadUpdatePlugin.Dispatcher
                ?? throw new InvalidOperationException("Plugin dispatcher was not captured."))
            .RegisterUpdate(() => { }));

        var failed = host.Load(typeof(FailingMainThreadUpdatePlugin));
        Assert(!failed.Succeeded, "The failing update plugin unexpectedly loaded.");
        Assert(mainThread.UpdateCount == 0, "A failed plugin left an update callback registered.");
    }

    private static void AppliesAndRemovesManagedDetour()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        Assert(ManagedHookTarget.Value() == 7, "Managed hook target did not begin with its original value.");
        using var handle = service.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
        Assert(ManagedHookTarget.Value() == 42, "Managed detour did not replace the target method.");

        handle.Dispose();
        handle.Dispose();

        Assert(ManagedHookTarget.Value() == 7, "Idempotent managed detour disposal did not restore the target method.");
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

    private static void DetoursMethodWithInParameters()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedInHookTarget), nameof(ManagedInHookTarget.Transform));
        var value = 2;

        Assert(ManagedInHookTarget.Transform(in value) == 7, "In-parameter target did not begin with its original value.");
        using (service.Detour(target, (ManagedInReplacement)ManagedInHookTarget.Replacement))
        {
            Assert(ManagedInHookTarget.Transform(in value) == 14, "In-parameter detour did not call the original method.");
        }

        Assert(ManagedInHookTarget.Transform(in value) == 7, "Disposing the in-parameter detour did not restore the target.");
    }

    private static void DetoursMethodWithByReferenceReturn()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedRefReturnHookTarget), nameof(ManagedRefReturnHookTarget.Value));

        ref var original = ref ManagedRefReturnHookTarget.Value();
        Assert(original == 7, "By-reference return target did not begin with its original value.");

        using (service.Detour(target, (ManagedRefReturnReplacement)ManagedRefReturnHookTarget.Replacement))
        {
            ref var hooked = ref ManagedRefReturnHookTarget.Value();
            Assert(hooked == 42, "By-reference detour did not return the replacement storage.");
            Assert(ManagedRefReturnHookTarget.OriginalValue == 12, "By-reference original call did not preserve its mutation.");

            hooked = 50;
            Assert(ManagedRefReturnHookTarget.ReplacementValue == 50, "Returned managed reference did not remain writable.");
        }

        ref var restored = ref ManagedRefReturnHookTarget.Value();
        Assert(restored == 12, "Disposing the by-reference detour did not restore the original storage.");
    }

    private static void RejectsGenericManagedDetourTargets()
    {
        var service = new RuntimeDetourHookService();
        var definition = GetRequiredMethod(typeof(ManagedGenericHookTarget), nameof(ManagedGenericHookTarget.Echo));
        var closedMethod = definition.MakeGenericMethod(typeof(int));
        var genericTypeMember = GetRequiredMethod(
            typeof(ManagedGenericTypeHookTarget<int>),
            nameof(ManagedGenericTypeHookTarget<int>.Echo));

        var openException = AssertThrows<ArgumentException>(
            () => service.Detour(
                definition,
                (ManagedGenericIntReplacement)ManagedGenericHookTarget.Replacement));
        var methodException = AssertThrows<NotSupportedException>(
            () => service.Detour(
                closedMethod,
                (ManagedGenericIntReplacement)ManagedGenericHookTarget.Replacement));
        var typeException = AssertThrows<NotSupportedException>(
            () => service.Detour(
                genericTypeMember,
                (Func<int, int>)ManagedHookTarget.ReplacementWithArgument));

        Assert(
            openException.Message.Contains("Open generic", StringComparison.Ordinal) &&
            methodException.Message.Contains("Generic methods", StringComparison.Ordinal) &&
            typeException.Message.Contains("generic types", StringComparison.Ordinal),
            "Generic target rejection did not explain the backend limitation.");
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
        Assert(
            refOutException.Message.Contains("ManagedRefOutHookTarget.TryTransform", StringComparison.Ordinal) &&
            refOutException.Message.Contains("System.Func<System.Boolean>", StringComparison.Ordinal),
            "Signature mismatch did not identify the target and actual delegate type.");
        AssertThrows<ArgumentException>(
            () => service.Detour(valueTypeTarget, (Func<ManagedValueHookTarget, int>)ManagedValueHookTarget.InvalidReplacement));
        AssertThrows<NotSupportedException>(
            () => service.Detour(valueTypeConstructorTarget, (Action)ManagedStaticConstructorHookTarget.Replacement));
        AssertThrows<NotSupportedException>(
            () => service.Detour(staticConstructorTarget, (Action)ManagedStaticConstructorHookTarget.Replacement));
    }

    private static void RejectsMulticastManagedDetourReplacement()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));
        Func<int> replacement = ManagedHookTarget.Replacement;
        replacement += ManagedHookTarget.Replacement;

        var exception = AssertThrows<ArgumentException>(() => service.Detour(target, replacement));
        Assert(
            exception.Message.Contains("exactly one invocation target", StringComparison.Ordinal),
            "Multicast replacement rejection did not explain the contract violation.");
        Assert(ManagedHookTarget.Value() == 7, "Rejected multicast replacement changed the target method.");
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

    private static void RetriesFailedPluginDetourRemoval()
    {
        var hooks = new TrackingHookService(failuresBeforeSuccess: new[] { 1 });
        using var host = CreateHost(hooks);

        var result = host.Load(typeof(RetryingCleanupPlugin));
        Assert(result.Succeeded, result.Error ?? "Retrying cleanup plugin did not load.");

        host.UnloadAll();

        var detour = hooks.Hooks.Single();
        Assert(RetryingCleanupPlugin.ObservedFirstFailure, "Plugin did not observe the first removal failure.");
        Assert(detour.DisposeAttempts == 2, "Failed plugin detour removal was not retried by context cleanup.");
        Assert(detour.IsDisposed, "Retried plugin detour removal did not complete.");
    }

    private static void ContinuesPluginDetourCleanupAfterFailure()
    {
        var hooks = new TrackingHookService(failuresBeforeSuccess: new[] { 0, int.MaxValue });
        var context = new TestContext(hooks);
        using var host = new PluginHost(context);

        var result = host.Load(typeof(MultipleCleanupPlugin));
        Assert(result.Succeeded, result.Error ?? "Multiple cleanup plugin did not load.");

        host.UnloadAll();

        Assert(hooks.Hooks.Count == 2, "Unexpected number of tracked cleanup detours.");
        Assert(hooks.Hooks[1].DisposeAttempts == 1, "Failing detour was not attempted during cleanup.");
        Assert(hooks.Hooks[0].IsDisposed, "Cleanup stopped before removing the remaining detour.");
        Assert(
            context.CapturedLogger.Messages.Any(message => message.Contains("hook cleanup failed", StringComparison.Ordinal)),
            "Aggregate hook cleanup failure was not logged.");
    }

    private static void AppliesAndRemovesIlHook()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        Assert(ManagedHookTarget.Value() == 7, "IL hook target did not begin with its original value.");
        using var handle = service.ModifyIl(target, ManagedIlHookTarget.ReplaceSevenWithFortyTwo);
        Assert(ManagedHookTarget.Value() == 42, "IL hook did not rewrite the target method.");

        handle.Dispose();
        handle.Dispose();

        Assert(ManagedHookTarget.Value() == 7, "Idempotent IL hook disposal did not restore the target method.");
    }

    private static void ChainsAndSelectivelyRemovesIlHooks()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        using (service.ModifyIl(target, ManagedIlHookTarget.AddTenBeforeReturn))
        {
            Assert(ManagedHookTarget.Value() == 17, "The first IL hook did not modify the return value.");

            using (service.ModifyIl(target, ManagedIlHookTarget.AddTwentyBeforeReturn))
            {
                Assert(ManagedHookTarget.Value() == 37, "IL hooks did not compose on one target.");
            }

            Assert(ManagedHookTarget.Value() == 17, "Removing one IL hook broke the remaining rewrite.");
        }

        Assert(ManagedHookTarget.Value() == 7, "Removing the IL hook chain did not restore the target.");
    }

    private static void CombinesIlHookWithManagedDetour()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        using (service.ModifyIl(target, ManagedIlHookTarget.AddTenBeforeReturn))
        {
            using (service.Detour(target, (ManagedHookReplacement)ManagedHookTarget.AddTwenty))
            {
                Assert(ManagedHookTarget.Value() == 37, "Managed detour did not observe the IL-rewritten original body.");
            }

            Assert(ManagedHookTarget.Value() == 17, "Removing the detour also removed the IL hook.");
        }

        Assert(ManagedHookTarget.Value() == 7, "Combined hook cleanup did not restore the target.");
    }

    private static void RewritesValueTypeConstructorIl()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredConstructor(typeof(ManagedIlValueTarget), typeof(int));

        Assert(new ManagedIlValueTarget(2).Value == 2, "Value-type constructor target began with an unexpected value.");
        using (service.ModifyIl(target, ManagedIlHookTarget.AddFiveBeforeFieldStore))
        {
            Assert(new ManagedIlValueTarget(2).Value == 7, "IL hook did not rewrite the value-type constructor.");
        }

        Assert(new ManagedIlValueTarget(2).Value == 2, "Removing the constructor IL hook did not restore its body.");
    }

    private static void RejectsUnsupportedIlHookTargets()
    {
        var service = new RuntimeDetourHookService();
        var validTarget = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));
        var openGeneric = GetRequiredMethod(typeof(ManagedGenericHookTarget), nameof(ManagedGenericHookTarget.Echo));
        var closedGeneric = openGeneric.MakeGenericMethod(typeof(int));
        var genericTypeMember = GetRequiredMethod(
            typeof(ManagedGenericTypeHookTarget<int>),
            nameof(ManagedGenericTypeHookTarget<int>.Echo));
        var abstractTarget = GetRequiredMethod(typeof(ManagedAbstractIlHookTarget), nameof(ManagedAbstractIlHookTarget.Value));
        var externalTarget = GetRequiredMethod(typeof(ManagedUnsupportedIlHookTarget), nameof(ManagedUnsupportedIlHookTarget.External));
        var varArgTarget = GetRequiredMethod(typeof(ManagedUnsupportedIlHookTarget), nameof(ManagedUnsupportedIlHookTarget.VarArg));
        var staticConstructor = typeof(ManagedStaticConstructorHookTarget).TypeInitializer
            ?? throw new InvalidOperationException("Static constructor IL hook target was not found.");

        AssertThrows<ArgumentNullException>(() => service.ModifyIl(null!, ManagedIlHookTarget.NoOp));
        AssertThrows<ArgumentNullException>(() => service.ModifyIl(validTarget, null!));
        AssertThrows<ArgumentException>(() => service.ModifyIl(abstractTarget, ManagedIlHookTarget.NoOp));
        AssertThrows<ArgumentException>(() => service.ModifyIl(openGeneric, ManagedIlHookTarget.NoOp));
        AssertThrows<NotSupportedException>(() => service.ModifyIl(closedGeneric, ManagedIlHookTarget.NoOp));
        AssertThrows<NotSupportedException>(() => service.ModifyIl(genericTypeMember, ManagedIlHookTarget.NoOp));
        var externalException = AssertThrows<NotSupportedException>(
            () => service.ModifyIl(externalTarget, ManagedIlHookTarget.NoOp));
        AssertThrows<NotSupportedException>(() => service.ModifyIl(varArgTarget, ManagedIlHookTarget.NoOp));
        AssertThrows<NotSupportedException>(() => service.ModifyIl(staticConstructor, ManagedIlHookTarget.NoOp));

        Assert(
            externalException.Message.Contains("does not have a managed IL body", StringComparison.Ordinal) &&
            externalException.Message.Contains(nameof(ManagedUnsupportedIlHookTarget.External), StringComparison.Ordinal),
            "Missing-body rejection did not identify the IL target and limitation.");
        Assert(ManagedHookTarget.Value() == 7, "Rejected IL targets changed a valid method.");
    }

    private static void RejectsMulticastIlManipulator()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));
        Action<ILContext> manipulator = ManagedIlHookTarget.AddTenBeforeReturn;
        manipulator += ManagedIlHookTarget.AddTwentyBeforeReturn;

        var exception = AssertThrows<ArgumentException>(() => service.ModifyIl(target, manipulator));
        Assert(
            exception.Message.Contains("exactly one invocation target", StringComparison.Ordinal),
            "Multicast IL manipulator rejection did not explain the contract violation.");
        Assert(ManagedHookTarget.Value() == 7, "Rejected multicast IL manipulator changed the target.");
    }

    private static void WrapsIlManipulatorFailures()
    {
        var service = new RuntimeDetourHookService();
        var target = GetRequiredMethod(typeof(ManagedHookTarget), nameof(ManagedHookTarget.Value));

        var exception = AssertThrows<InsiderHookException>(
            () => service.ModifyIl(target, ManagedIlHookTarget.Fail));

        Assert(
            exception.Message.Contains("Could not apply IL hook", StringComparison.Ordinal) &&
            exception.Message.Contains("ManagedHookTarget.Value", StringComparison.Ordinal) &&
            exception.ToString().Contains("Expected IL manipulator failure", StringComparison.Ordinal),
            "IL manipulator failure did not preserve stable target-aware diagnostics.");
        Assert(ManagedHookTarget.Value() == 7, "Failed IL manipulation changed the target.");
    }

    private static void RemovesPluginIlHooksDuringUnload()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var result = host.Load(typeof(IlHookingPlugin));

        Assert(result.Succeeded, result.Error ?? "IL-hooking plugin did not load.");
        Assert(ManagedHookTarget.Value() == 17, "Plugin-owned IL hook was not applied.");

        host.UnloadAll();

        Assert(ManagedHookTarget.Value() == 7, "Plugin-owned IL hook remained after unload.");
    }

    private static void RemovesPluginIlHooksAfterFailedLoad()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var result = host.Load(typeof(FailingIlHookingPlugin));

        Assert(!result.Succeeded, "Failing IL-hooking plugin was reported as loaded.");
        Assert(ManagedHookTarget.Value() == 7, "Failed plugin load left its IL hook applied.");
    }

    private static void PreservesOtherPluginIlHooksAfterFailedLoad()
    {
        using var host = CreateHost(new RuntimeDetourHookService());

        var loaded = host.Load(typeof(IlHookingPlugin));
        Assert(loaded.Succeeded, loaded.Error ?? "IL-hooking plugin did not load.");
        Assert(ManagedHookTarget.Value() == 17, "Existing plugin IL hook was not applied.");

        var failed = host.Load(typeof(FailingIlChainPlugin));
        Assert(!failed.Succeeded, "Failing IL chain plugin was reported as loaded.");
        Assert(ManagedHookTarget.Value() == 17, "Failed plugin cleanup changed another plugin's IL hook.");

        host.UnloadAll();
        Assert(ManagedHookTarget.Value() == 7, "Unloading the remaining IL hook did not restore the target.");
    }

    private static void RetriesFailedPluginIlHookRemoval()
    {
        var hooks = new TrackingHookService(failuresBeforeSuccess: new[] { 1 });
        using var host = CreateHost(hooks);

        var result = host.Load(typeof(RetryingIlCleanupPlugin));
        Assert(result.Succeeded, result.Error ?? "Retrying IL cleanup plugin did not load.");

        host.UnloadAll();

        var hook = hooks.Hooks.Single();
        Assert(RetryingIlCleanupPlugin.ObservedFirstFailure, "Plugin did not observe the first IL removal failure.");
        Assert(hook.Kind == "IL hook", "Tracking service did not record an IL hook.");
        Assert(hook.DisposeAttempts == 2, "Failed plugin IL hook removal was not retried.");
        Assert(hook.IsDisposed, "Retried plugin IL hook removal did not complete.");
    }

    private static void ContinuesPluginIlHookCleanupAfterFailure()
    {
        var hooks = new TrackingHookService(failuresBeforeSuccess: new[] { 0, int.MaxValue });
        var context = new TestContext(hooks);
        using var host = new PluginHost(context);

        var result = host.Load(typeof(MultipleIlCleanupPlugin));
        Assert(result.Succeeded, result.Error ?? "Multiple IL cleanup plugin did not load.");

        host.UnloadAll();

        Assert(hooks.Hooks.Count == 2, "Unexpected number of tracked IL cleanup hooks.");
        Assert(hooks.Hooks[1].DisposeAttempts == 1, "Failing IL hook was not attempted during cleanup.");
        Assert(hooks.Hooks[0].IsDisposed, "IL cleanup stopped before removing the remaining hook.");
        Assert(
            context.CapturedLogger.Messages.Any(message => message.Contains("hook cleanup failed", StringComparison.Ordinal)),
            "Aggregate IL hook cleanup failure was not logged.");
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

    private static void HandlesDependenciesOnDisabledPlugins()
    {
        var context = new TestContext();
        using var host = new PluginHost(context);

        var results = host.Load(
            new[]
            {
                typeof(FoundationPlugin),
                typeof(DependentPlugin),
                typeof(OptionalDependencyPlugin),
            },
            new[] { "DEV.INSIDER.TESTS.FOUNDATION" });

        Assert(results.Count == 2, "A disabled plugin unexpectedly produced a load result.");
        Assert(results.Count(result => result.Succeeded) == 1, "Optional dependency handling was incorrect.");
        Assert(
            results.Any(result =>
                result.Error?.Contains("dev.insider.tests.foundation (disabled)", StringComparison.Ordinal) == true),
            "A required disabled dependency was not diagnosed.");
        Assert(
            host.LoadedPlugins.Count == 1 &&
            host.LoadedPlugins.First().Id == "dev.insider.tests.optional",
            "A disabled optional dependency blocked plugin activation.");
        Assert(
            PluginGraphEvents.SequenceEqual(new[] { "optional" }),
            "A disabled provider or its required dependant executed Load().");
        Assert(
            context.CapturedLogger.Messages.Contains("Skipped disabled plugin dev.insider.tests.foundation 1.0.0."),
            "The disabled provider was not logged.");
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
        var loadedMarkerText = File.ReadAllText(loadedMarker);
        Assert(loadedMarkerText.Contains("Backend=UnityMono", StringComparison.Ordinal), "Fixture received the wrong runtime context.");
        Assert(loadedMarkerText.Contains($"PluginDirectory={fixture.PluginDirectory}", StringComparison.Ordinal), "Fixture received the wrong plugin directory.");
        Assert(
            loadedMarkerText.Contains(
                $"ConfigDirectory={Path.Combine(fixture.ConfigDirectory, "dev.insider.tests.bootstrap-fixture")}",
                StringComparison.Ordinal),
            "Fixture received the wrong configuration directory.");
        Assert(
            loadedMarkerText.Contains(
                $"DataDirectory={Path.Combine(fixture.DataDirectory, "dev.insider.tests.bootstrap-fixture")}",
                StringComparison.Ordinal),
            "Fixture received the wrong data directory.");
        Assert(loadedMarkerText.Contains("Dependency=dependency-v1", StringComparison.Ordinal), "Fixture dependency was not resolved.");
        Assert(Directory.Exists(fixture.ConfigDirectory), "Bootstrap did not create the configuration root.");
        Assert(Directory.Exists(fixture.DataDirectory), "Bootstrap did not create the data root.");

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

    private static void SkipsPluginsInBootstrapDisableList()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: true);
        fixture.InstallPluginFixture();
        fixture.WriteDisabledPluginList(
            "# One plugin id per line.",
            string.Empty,
            "  DEV.INSIDER.TESTS.BOOTSTRAP-FIXTURE  ",
            "dev.insider.tests.bootstrap-fixture");

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(result.IsSupported, "Unity Mono fixture was not recognized as supported.");
        Assert(result.LoadedPluginCount == 0, "A disabled plugin was loaded.");
        Assert(result.FailedPluginCount == 0, "A disabled plugin was reported as failed.");
        Assert(
            !File.Exists(Path.Combine(result.InsiderDirectory, "fixture-loaded.txt")),
            "A disabled plugin executed Load().");

        var log = File.ReadAllText(result.LogPath);
        Assert(
            log.Contains("Read 1 disabled plugin id(s)", StringComparison.Ordinal),
            "Comments, blanks, duplicates, or case variants were not normalized.");
        Assert(
            log.Contains("Skipped disabled plugin dev.insider.tests.bootstrap-fixture 1.0.0.", StringComparison.Ordinal),
            "The disabled plugin was not diagnosed.");
        Assert(
            log.Contains("Plugin scan completed: 0 loaded, 0 failed.", StringComparison.Ordinal),
            "The bootstrap summary counted a disabled plugin as a failure.");
    }

    private static void ResolvesHostDependencyFromCore()
    {
        using var fixture = BootstrapFixtureWorkspace.Create(withMonoRuntime: true);
        fixture.InstallPluginFixture(includeDependency: false);
        fixture.InstallDependencyInCore();

        using var session = new BootstrapSession();
        var result = session.Start(fixture.GameDirectory);

        Assert(result.LoadedPluginCount == 1, "Plugin using a host dependency was not loaded.");
        Assert(result.FailedPluginCount == 0, "Host dependency resolution unexpectedly failed.");
        Assert(File.Exists(Path.Combine(result.InsiderDirectory, "fixture-loaded.txt")), "Plugin did not run with its host dependency.");
        Assert(
            File.ReadAllText(result.LogPath).Contains(fixture.CoreDirectory, StringComparison.OrdinalIgnoreCase),
            "Host dependency resolution was not logged from the Insider core.");
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
        var configDirectory = Path.Combine(fixture.GameDirectory, "Insider", "config");
        Assert(Directory.Exists(configDirectory), "Plugin configuration directory was not created.");
        var dataDirectory = Path.Combine(fixture.GameDirectory, "Insider", "data");
        Assert(Directory.Exists(dataDirectory), "Plugin data directory was not created.");
        var disabledPluginPath = Path.Combine(configDirectory, DisabledPluginList.FileName);
        File.WriteAllText(disabledPluginPath, "dev.insider.tests.bootstrap-fixture");
        var pluginDataPath = Path.Combine(dataDirectory, "user-state.txt");
        File.WriteAllText(pluginDataPath, "user-state");

        var removed = installer.Uninstall(fixture.GameExecutable);

        Assert(removed.State == InsiderInstallationState.NotInstalled, "Uninstall did not complete.");
        Assert(File.ReadAllText(Path.Combine(fixture.GameDirectory, "version.dll")) == "original-proxy", "Original proxy was not restored.");
        Assert(!File.Exists(Path.Combine(fixture.GameDirectory, "Insider", "install.json")), "Manifest was not removed.");
        Assert(File.Exists(disabledPluginPath), "Uninstall removed the user-owned disabled-plugin list.");
        Assert(File.Exists(pluginDataPath), "Uninstall removed user-owned plugin data.");
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

    private static void ManagesDisabledPluginsThroughCli()
    {
        using var fixture = InstallationFixture.Create(withExistingProxy: false);
        new InsiderInstaller().Install(fixture.GameExecutable, fixture.BundleDirectory);

        var disabledPath = Path.Combine(
            fixture.GameDirectory,
            DisabledPluginManager.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllLines(
            disabledPath,
            new[]
            {
                "# Keep this explanation",
                "dev.insider.tests.zeta",
                " DEV.INSIDER.TESTS.ZETA ",
                string.Empty,
                "dev.insider.tests.alpha",
            });

        Assert(
            CliProgram.Run(new[] { "plugins", "disabled", fixture.GameExecutable }) == 0,
            "The disabled-plugin listing command failed.");

        var unchanged = File.ReadAllText(disabledPath);
        Assert(
            CliProgram.Run(new[] { "plugins", "disable", fixture.GameExecutable, "DEV.INSIDER.TESTS.ALPHA" }) == 0,
            "Disabling an already-disabled plugin failed.");
        Assert(File.ReadAllText(disabledPath) == unchanged, "An idempotent disable rewrote the user file.");

        Assert(
            CliProgram.Run(new[] { "plugins", "disable", fixture.GameExecutable, "dev.insider.tests.middle" }) == 0,
            "Disabling a plugin failed.");
        var afterDisable = File.ReadAllText(disabledPath);
        Assert(afterDisable.Contains("# Keep this explanation", StringComparison.Ordinal), "Disabling a plugin removed a comment.");
        Assert(afterDisable.Contains("dev.insider.tests.middle", StringComparison.Ordinal), "The plugin ID was not added.");

        Assert(
            CliProgram.Run(new[] { "plugins", "enable", fixture.GameExecutable, "DEV.INSIDER.TESTS.ZETA" }) == 0,
            "Enabling a plugin failed.");
        var afterEnable = File.ReadAllLines(disabledPath);
        Assert(afterEnable.Contains("# Keep this explanation"), "Enabling a plugin removed a comment.");
        Assert(
            !afterEnable.Any(line => string.Equals(line.Trim(), "dev.insider.tests.zeta", StringComparison.OrdinalIgnoreCase)),
            "Enabling a plugin left a case-insensitive duplicate in the list.");

        var unchangedAfterEnable = File.ReadAllText(disabledPath);
        Assert(
            CliProgram.Run(new[] { "plugins", "enable", fixture.GameExecutable, "dev.insider.tests.zeta" }) == 0,
            "Enabling an already-enabled plugin failed.");
        Assert(File.ReadAllText(disabledPath) == unchangedAfterEnable, "An idempotent enable rewrote the user file.");

        var disabled = new DisabledPluginManager().GetDisabled(fixture.GameExecutable);
        Assert(
            disabled.SequenceEqual(new[] { "dev.insider.tests.alpha", "dev.insider.tests.middle" }),
            "The disabled-plugin list was not normalized and sorted.");

        AssertThrows<InsiderInstallationException>(() =>
            CliProgram.Run(new[] { "plugins", "disable", fixture.GameExecutable, "# invalid" }));

        using var notInstalled = InstallationFixture.Create(withExistingProxy: false);
        AssertThrows<InsiderInstallationException>(() =>
            CliProgram.Run(new[] { "plugins", "disabled", notInstalled.GameExecutable }));
    }

    private static void DiagnosesGamePluginsWithoutActivation()
    {
        using var fixture = InstallationFixture.Create(withExistingProxy: false);
        new InsiderInstaller().Install(fixture.GameExecutable, fixture.BundleDirectory);

        var pluginDirectory = Path.Combine(fixture.GameDirectory, "Insider", "plugins");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "diagnostics", "Insider.DiagnosticFixture.dll"),
            Path.Combine(pluginDirectory, "Insider.DiagnosticFixture.dll"));
        File.WriteAllLines(
            Path.Combine(
                fixture.GameDirectory,
                DisabledPluginManager.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            new[]
            {
                "dev.insider.tests.diagnostic-disabled",
                "dev.insider.tests.stale-disabled-id",
            });

        var report = GameDiagnoser.Diagnose(fixture.GameExecutable);
        Assert(report.Plugins.Count == 11, "The diagnostic catalog did not find every fixture plugin.");
        Assert(
            report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-foundation").State ==
                PluginDiagnosticState.Ready,
            "A valid foundation plugin was not ready.");
        var ready = report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-ready");
        Assert(ready.State == PluginDiagnosticState.Ready, "A plugin with a satisfied dependency was not ready.");
        Assert(
            ready.Dependencies.Single().Status.Contains("ready (1.2.0)", StringComparison.Ordinal),
            "A satisfied minimum version was not rendered readably.");
        var optional = report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-optional");
        Assert(optional.State == PluginDiagnosticState.Ready, "A missing optional dependency blocked a plugin.");
        Assert(optional.Dependencies.Single().Status.Contains("allowed", StringComparison.Ordinal),
            "A missing optional dependency was not explained.");
        Assert(
            report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-disabled").State ==
                PluginDiagnosticState.Disabled,
            "The disabled plugin was not identified.");
        Assert(
            report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-broken").Issues
                .Any(issue => issue.Contains("missing", StringComparison.Ordinal)),
            "A missing required dependency was not diagnosed.");
        Assert(
            report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-needs-disabled").Issues
                .Any(issue => issue.Contains("disabled", StringComparison.Ordinal)),
            "A disabled required dependency was not diagnosed.");
        Assert(
            report.Plugins.Single(plugin => plugin.Id == "dev.insider.tests.diagnostic-needs-newer").Issues
                .Any(issue => issue.Contains("found 1.2.0", StringComparison.Ordinal)),
            "A minimum-version mismatch was not diagnosed.");
        Assert(
            report.Plugins.Count(plugin => plugin.Id == "dev.insider.tests.diagnostic-duplicate" &&
                plugin.State == PluginDiagnosticState.Problem) == 2,
            "Duplicate plugin IDs were not diagnosed.");
        Assert(
            report.Plugins.Count(plugin => plugin.Issues.Any(issue => issue.Contains("cycle", StringComparison.Ordinal))) == 2,
            "The required dependency cycle was not diagnosed.");
        Assert(
            report.Notes.Single().Contains("stale-disabled-id", StringComparison.Ordinal),
            "A stale disabled ID was not reported as a note.");
        Assert(report.HasProblems, "The broken diagnostic fixture unexpectedly produced a clean report.");
        Assert(
            CliProgram.Run(new[] { "diagnose", fixture.GameExecutable }) == 1,
            "The diagnose command did not return a failing exit code for detected problems.");
    }

    private static void DispatchesUnityMainThreadCallbacks()
    {
        _ = ManagedMainThreadFixture.UnityCoreAssembly;
        var hooks = new CapturingMainThreadHookService();
        var logger = new TestLogger();
        var events = new List<string>();
        var dispatcher = new UnityMonoMainThread(hooks, logger);

        dispatcher.Start();
        Assert(hooks.Target?.Name == "ExecuteTasks", "The Unity synchronization pump was not targeted.");
        Assert(!dispatcher.IsReady, "The dispatcher was ready before the Unity pump ran.");

        dispatcher.Post(() =>
        {
            events.Add("first");
            dispatcher.Post(() => events.Add("next-frame"));
        });
        dispatcher.Post(() => events.Add("second"));

        hooks.InvokePump();
        Assert(dispatcher.IsReady && dispatcher.IsCurrent, "The Unity pump did not identify the current main thread.");
        Assert(events.SequenceEqual(new[] { "first", "second" }), "Callbacks did not run in FIFO snapshot order.");

        hooks.InvokePump();
        Assert(events.SequenceEqual(new[] { "first", "second", "next-frame" }), "A reentrant callback did not wait for the next pump.");

        var updateCount = 0;
        var lateUpdateCount = 0;
        IDisposable? lateUpdate = null;
        using var update = dispatcher.RegisterUpdate(() =>
        {
            updateCount++;
            if (lateUpdate is null)
            {
                lateUpdate = dispatcher.RegisterUpdate(() => lateUpdateCount++);
            }
        });

        hooks.InvokePump();
        Assert(updateCount == 1 && lateUpdateCount == 0, "A newly registered update ran in the current pump.");
        hooks.InvokePump();
        Assert(updateCount == 2 && lateUpdateCount == 1, "Update callbacks did not run once per pump.");

        update.Dispose();
        lateUpdate?.Dispose();
        hooks.InvokePump();
        Assert(updateCount == 2 && lateUpdateCount == 1, "A disposed update callback ran again.");

        dispatcher.Post(() => throw new InvalidOperationException("Expected callback failure."));
        dispatcher.Post(() => events.Add("after-failure"));
        hooks.InvokePump();
        Assert(events[^1] == "after-failure", "A failed callback stopped the remaining dispatcher callbacks.");
        Assert(
            logger.Messages.Contains("A Unity main-thread callback failed."),
            "The dispatcher did not log a failed callback.");

        using var failingUpdate = dispatcher.RegisterUpdate(
            () => throw new InvalidOperationException("Expected update failure."));
        var successfulUpdateCount = 0;
        using var successfulUpdate = dispatcher.RegisterUpdate(() => successfulUpdateCount++);
        hooks.InvokePump();
        Assert(successfulUpdateCount == 1, "A failed update callback stopped another update callback.");
        Assert(
            logger.Messages.Contains("A Unity update callback failed."),
            "The dispatcher did not log a failed update callback.");

        dispatcher.Dispose();
        Assert(hooks.Hook.DisposeCount == 1, "The Unity pump hook was not removed exactly once.");
        AssertThrows<ObjectDisposedException>(() => dispatcher.Post(() => { }));
        AssertThrows<ObjectDisposedException>(() => dispatcher.RegisterUpdate(() => { }));
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
        ManagedRefReturnHookTarget.Reset();
        RetryingCleanupPlugin.ObservedFirstFailure = false;
        RetryingIlCleanupPlugin.ObservedFirstFailure = false;
        MainThreadCallbackPlugin.SuccessCount = 0;
        MainThreadCancellationPlugin.CallbackCount = 0;
        MainThreadCancellationPlugin.Dispatcher = null;
        MainThreadUpdatePlugin.SuccessCount = 0;
        MainThreadUpdatePlugin.Dispatcher = null;
        MainThreadUpdatePlugin.Handle = null;
        LifecycleEvents.Clear();
        PluginGraphEvents.Clear();
        DirectoryPluginA.Context = null;
        DirectoryPluginB.Context = null;
        UnsafeDirectoryPlugin.Context = null;
        TestContext.ResetDirectories();
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

    public string CoreDirectory => Path.Combine(GameDirectory, "Insider", "core");

    public string ConfigDirectory => Path.Combine(GameDirectory, "Insider", "config");

    public string DataDirectory => Path.Combine(GameDirectory, "Insider", "data");

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

    public void InstallDependencyInCore()
    {
        Directory.CreateDirectory(CoreDirectory);
        File.Copy(
            GetFixturePath("dependencies", "v1", "Insider.DependencyFixture.dll"),
            Path.Combine(CoreDirectory, "Insider.DependencyFixture.dll"));
    }

    public void WriteDisabledPluginList(params string[] lines)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllLines(
            Path.Combine(ConfigDirectory, DisabledPluginList.FileName),
            lines);
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

[InsiderPlugin("dev.insider.tests.directories-a", "Directories A", "1.0.0")]
public sealed class DirectoryPluginA : IInsiderPlugin
{
    public static IInsiderContext? Context { get; set; }

    public void Load(IInsiderContext context)
    {
        Context = context;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.directories-b", "Directories B", "1.0.0")]
public sealed class DirectoryPluginB : IInsiderPlugin
{
    public static IInsiderContext? Context { get; set; }

    public void Load(IInsiderContext context)
    {
        Context = context;
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("../dev.insider.tests.directories-unsafe", "Unsafe Directories", "1.0.0")]
public sealed class UnsafeDirectoryPlugin : IInsiderPlugin
{
    public static IInsiderContext? Context { get; set; }

    public void Load(IInsiderContext context)
    {
        Context = context;
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
    private static readonly string RootDirectory = Path.Combine(
        Path.GetTempPath(),
        "insider-managed-context-tests",
        Guid.NewGuid().ToString("N"));

    public TestContext(
        IInsiderHookService? hooks = null,
        IInsiderMainThread? mainThread = null)
    {
        GameDirectory = Path.Combine(RootDirectory, "game");
        InsiderDirectory = Path.Combine(GameDirectory, "Insider");
        PluginDirectory = Path.Combine(InsiderDirectory, "plugins");
        ConfigDirectory = Path.Combine(InsiderDirectory, "config");
        DataDirectory = Path.Combine(InsiderDirectory, "data");
        Directory.CreateDirectory(PluginDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);

        CapturedLogger = new TestLogger();
        Logger = CapturedLogger;
        MainThread = mainThread ?? new NoOpMainThread();
        Hooks = hooks ?? new NoOpHookService();
    }

    public string GameDirectory { get; }

    public string InsiderDirectory { get; }

    public string PluginDirectory { get; }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public IInsiderLogger Logger { get; }

    public TestLogger CapturedLogger { get; }

    public IInsiderRuntimeInfo Runtime { get; } = new TestRuntimeInfo();

    public IInsiderMainThread MainThread { get; }

    public IInsiderHookService Hooks { get; }

    public static void ResetDirectories()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}

internal sealed class NoOpMainThread : IInsiderMainThread
{
    public bool IsReady => false;

    public bool IsCurrent => false;

    public void Post(Action callback)
    {
        _ = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public IDisposable RegisterUpdate(Action callback)
    {
        _ = callback ?? throw new ArgumentNullException(nameof(callback));
        return new EmptyRegistration();
    }

    private sealed class EmptyRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class ManualMainThread : IInsiderMainThread
{
    private readonly Queue<Action> _callbacks = new Queue<Action>();
    private readonly List<UpdateRegistration> _updates = new List<UpdateRegistration>();
    private bool _isCurrent;

    public bool IsReady => true;

    public bool IsCurrent => _isCurrent;

    public int UpdateCount => _updates.Count;

    public void Post(Action callback)
    {
        _callbacks.Enqueue(callback ?? throw new ArgumentNullException(nameof(callback)));
    }

    public IDisposable RegisterUpdate(Action callback)
    {
        var registration = new UpdateRegistration(
            this,
            callback ?? throw new ArgumentNullException(nameof(callback)));
        _updates.Add(registration);
        return registration;
    }

    public void Drain()
    {
        var callbacks = _callbacks.ToArray();
        _callbacks.Clear();
        var updates = _updates.ToArray();
        _isCurrent = true;
        try
        {
            foreach (var callback in callbacks)
            {
                callback();
            }

            foreach (var update in updates)
            {
                update.Invoke();
            }
        }
        finally
        {
            _isCurrent = false;
        }
    }

    private void Remove(UpdateRegistration registration)
    {
        _updates.Remove(registration);
    }

    private sealed class UpdateRegistration : IDisposable
    {
        private readonly Action _callback;
        private readonly ManualMainThread _owner;
        private bool _disposed;

        public UpdateRegistration(ManualMainThread owner, Action callback)
        {
            _owner = owner;
            _callback = callback;
        }

        public void Invoke()
        {
            if (!_disposed)
            {
                _callback();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Remove(this);
        }
    }
}

internal sealed class CapturingMainThreadHookService : IInsiderHookService
{
    public MethodBase? Target { get; private set; }

    public Delegate? Replacement { get; private set; }

    public CapturedMainThreadHook Hook { get; } = new CapturedMainThreadHook();

    public int OriginalCalls { get; private set; }

    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        Target = target;
        Replacement = replacement;
        return Hook;
    }

    public IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator)
    {
        throw new NotSupportedException("The main-thread fixture does not install IL hooks.");
    }

    public void InvokePump()
    {
        var replacement = Replacement
            ?? throw new InvalidOperationException("The Unity pump replacement was not captured.");
        var originalType = replacement.Method.GetParameters()[0].ParameterType;
        var originalMethod = GetType().GetMethod(
            nameof(CallOriginal),
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The Unity pump original callback was not found.");
        var original = Delegate.CreateDelegate(originalType, this, originalMethod);
        replacement.DynamicInvoke(original);
    }

    private void CallOriginal()
    {
        OriginalCalls++;
    }

    internal sealed class CapturedMainThreadHook : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (DisposeCount == 0)
            {
                DisposeCount++;
            }
        }
    }
}

internal static class ManagedMainThreadFixture
{
    public static Assembly UnityCoreAssembly { get; } = CreateUnityCoreAssembly();

    private static Assembly CreateUnityCoreAssembly()
    {
        var assembly = ReflectionEmit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("UnityEngine.CoreModule"),
            ReflectionEmit.AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("UnityEngine.CoreModule.dll");
        var type = module.DefineType(
            "UnityEngine.UnitySynchronizationContext",
            TypeAttributes.NotPublic | TypeAttributes.Sealed);
        var executeTasks = type.DefineMethod(
            "ExecuteTasks",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            typeof(void),
            Type.EmptyTypes);
        executeTasks.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        _ = type.CreateType();
        return assembly;
    }
}

internal sealed class NoOpHookService : IInsiderHookService
{
    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        return new NoOpHook();
    }

    public IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator)
    {
        return new NoOpHook();
    }

    private sealed class NoOpHook : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class TrackingHookService : IInsiderHookService
{
    private readonly Queue<int> _failuresBeforeSuccess;

    public TrackingHookService(IEnumerable<int> failuresBeforeSuccess)
    {
        _failuresBeforeSuccess = new Queue<int>(failuresBeforeSuccess);
    }

    public List<TrackingHook> Hooks { get; } = new List<TrackingHook>();

    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        return Track("managed detour");
    }

    public IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator)
    {
        return Track("IL hook");
    }

    private IDisposable Track(string kind)
    {
        if (_failuresBeforeSuccess.Count == 0)
        {
            throw new InvalidOperationException("No tracking hook behavior was configured.");
        }

        var hook = new TrackingHook(kind, _failuresBeforeSuccess.Dequeue());
        Hooks.Add(hook);
        return hook;
    }

    internal sealed class TrackingHook : IDisposable
    {
        private int _failuresRemaining;

        public TrackingHook(string kind, int failuresBeforeSuccess)
        {
            Kind = kind;
            _failuresRemaining = failuresBeforeSuccess;
        }

        public string Kind { get; }

        public int DisposeAttempts { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            DisposeAttempts++;
            if (_failuresRemaining > 0)
            {
                _failuresRemaining--;
                throw new InvalidOperationException("Expected tracking hook removal failure.");
            }

            IsDisposed = true;
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

internal static class ManagedIlHookTarget
{
    public static void ReplaceSevenWithFortyTwo(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
            MoveType.Before,
            instruction => instruction.MatchLdcI4(7)))
        {
            throw new InvalidOperationException("Expected integer constant 7 was not found.");
        }

        cursor.Remove();
        cursor.Emit(OpCodes.Ldc_I4, 42);
    }

    public static void AddTenBeforeReturn(ILContext il)
    {
        AddBeforeReturn(il, 10);
    }

    public static void AddTwentyBeforeReturn(ILContext il)
    {
        AddBeforeReturn(il, 20);
    }

    public static void AddOneHundredBeforeReturn(ILContext il)
    {
        AddBeforeReturn(il, 100);
    }

    public static void AddFiveBeforeFieldStore(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
            MoveType.Before,
            instruction => instruction.OpCode == OpCodes.Stfld))
        {
            throw new InvalidOperationException("Expected value-type field store was not found.");
        }

        cursor.Emit(OpCodes.Ldc_I4, 5);
        cursor.Emit(OpCodes.Add);
    }

    public static void NoOp(ILContext il)
    {
    }

    public static void Fail(ILContext il)
    {
        throw new InvalidOperationException("Expected IL manipulator failure.");
    }

    private static void AddBeforeReturn(ILContext il, int value)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
            MoveType.Before,
            instruction => instruction.OpCode == OpCodes.Ret))
        {
            throw new InvalidOperationException("Expected return instruction was not found.");
        }

        cursor.Emit(OpCodes.Ldc_I4, value);
        cursor.Emit(OpCodes.Add);
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

internal delegate int ManagedInOriginal(in int value);

internal delegate int ManagedInReplacement(ManagedInOriginal original, in int value);

internal static class ManagedInHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Transform(in int value)
    {
        return value + 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(ManagedInOriginal original, in int value)
    {
        return original(in value) * 2;
    }
}

internal delegate ref int ManagedRefReturnOriginal();

internal delegate ref int ManagedRefReturnReplacement(ManagedRefReturnOriginal original);

internal static class ManagedRefReturnHookTarget
{
    private static int _originalValue = 7;
    private static int _replacementValue = 42;

    public static int OriginalValue => _originalValue;

    public static int ReplacementValue => _replacementValue;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ref int Value()
    {
        return ref _originalValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ref int Replacement(ManagedRefReturnOriginal original)
    {
        ref var originalValue = ref original();
        originalValue += 5;
        return ref _replacementValue;
    }

    public static void Reset()
    {
        _originalValue = 7;
        _replacementValue = 42;
    }
}

internal delegate int ManagedGenericIntOriginal(int value);

internal delegate int ManagedGenericIntReplacement(ManagedGenericIntOriginal original, int value);

internal static class ManagedGenericHookTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Echo<T>(T value)
    {
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Replacement(ManagedGenericIntOriginal original, int value)
    {
        return original(value) + 10;
    }
}

internal static class ManagedGenericTypeHookTarget<T>
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Echo(T value)
    {
        return value;
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

internal struct ManagedIlValueTarget
{
    private int _value;

    public ManagedIlValueTarget(int value)
    {
        _value = value;
    }

    public int Value => _value;
}

internal abstract class ManagedAbstractIlHookTarget
{
    public abstract int Value();
}

internal static class ManagedUnsupportedIlHookTarget
{
    [DllImport("kernel32.dll")]
    public static extern int External();

    public static int VarArg(int value, __arglist)
    {
        return value;
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

[InsiderPlugin("dev.insider.tests.il-hooking", "IL Hooking", "1.0.0")]
public sealed class IlHookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed IL hook target was not found.");
        _ = context.Hooks.ModifyIl(target, ManagedIlHookTarget.AddTenBeforeReturn);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing-il-hooking", "Failing IL Hooking", "1.0.0")]
public sealed class FailingIlHookingPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed IL hook target was not found.");
        _ = context.Hooks.ModifyIl(target, ManagedIlHookTarget.AddTenBeforeReturn);
        throw new InvalidOperationException("Expected failure after applying an IL hook.");
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.failing-il-chain", "Failing IL Chain", "1.0.0")]
public sealed class FailingIlChainPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed IL hook target was not found.");
        _ = context.Hooks.ModifyIl(target, ManagedIlHookTarget.AddOneHundredBeforeReturn);
        throw new InvalidOperationException("Expected failure after extending an IL hook chain.");
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.retrying-cleanup", "Retrying Cleanup", "1.0.0")]
public sealed class RetryingCleanupPlugin : IInsiderPlugin
{
    private IDisposable? _detour;

    public static bool ObservedFirstFailure { get; set; }

    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _detour = context.Hooks.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
    }

    public void Unload()
    {
        try
        {
            _detour?.Dispose();
        }
        catch (InvalidOperationException)
        {
            ObservedFirstFailure = true;
        }
        finally
        {
            _detour = null;
        }
    }
}

[InsiderPlugin("dev.insider.tests.retrying-il-cleanup", "Retrying IL Cleanup", "1.0.0")]
public sealed class RetryingIlCleanupPlugin : IInsiderPlugin
{
    private IDisposable? _hook;

    public static bool ObservedFirstFailure { get; set; }

    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed IL hook target was not found.");
        _hook = context.Hooks.ModifyIl(target, ManagedIlHookTarget.NoOp);
    }

    public void Unload()
    {
        try
        {
            _hook?.Dispose();
        }
        catch (InvalidOperationException)
        {
            ObservedFirstFailure = true;
        }
        finally
        {
            _hook = null;
        }
    }
}

[InsiderPlugin("dev.insider.tests.multiple-cleanup", "Multiple Cleanup", "1.0.0")]
public sealed class MultipleCleanupPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed hook target was not found.");
        _ = context.Hooks.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
        _ = context.Hooks.Detour(target, (Func<int>)ManagedHookTarget.Replacement);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.multiple-il-cleanup", "Multiple IL Cleanup", "1.0.0")]
public sealed class MultipleIlCleanupPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        var target = typeof(ManagedHookTarget).GetMethod(nameof(ManagedHookTarget.Value))
            ?? throw new InvalidOperationException("Managed IL hook target was not found.");
        _ = context.Hooks.ModifyIl(target, ManagedIlHookTarget.NoOp);
        _ = context.Hooks.ModifyIl(target, ManagedIlHookTarget.NoOp);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.main-thread-callback", "Main Thread Callback", "1.0.0")]
public sealed class MainThreadCallbackPlugin : IInsiderPlugin
{
    public static int SuccessCount { get; set; }

    public void Load(IInsiderContext context)
    {
        context.MainThread.Post(() => throw new InvalidOperationException("Expected plugin callback failure."));
        context.MainThread.Post(() => SuccessCount++);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.main-thread-cancellation", "Main Thread Cancellation", "1.0.0")]
public sealed class MainThreadCancellationPlugin : IInsiderPlugin
{
    public static int CallbackCount { get; set; }

    public static IInsiderMainThread? Dispatcher { get; set; }

    public void Load(IInsiderContext context)
    {
        Dispatcher = context.MainThread;
        context.MainThread.Post(() => CallbackCount++);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.main-thread-update", "Main Thread Update", "1.0.0")]
public sealed class MainThreadUpdatePlugin : IInsiderPlugin
{
    public static IInsiderMainThread? Dispatcher { get; set; }

    public static IDisposable? Handle { get; set; }

    public static int SuccessCount { get; set; }

    public void Load(IInsiderContext context)
    {
        Dispatcher = context.MainThread;
        _ = context.MainThread.RegisterUpdate(
            () => throw new InvalidOperationException("Expected plugin update failure."));
        Handle = context.MainThread.RegisterUpdate(() => SuccessCount++);
    }

    public void Unload()
    {
    }
}

[InsiderPlugin("dev.insider.tests.main-thread-update-failure", "Main Thread Update Failure", "1.0.0")]
public sealed class FailingMainThreadUpdatePlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        _ = context.MainThread.RegisterUpdate(() => { });
        throw new InvalidOperationException("Expected failure after registering an update callback.");
    }

    public void Unload()
    {
    }
}

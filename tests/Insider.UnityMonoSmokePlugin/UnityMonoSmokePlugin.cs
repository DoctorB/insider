using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Insider;

namespace Insider.UnityMonoSmokePlugin;

[InsiderPlugin("dev.insider.tests.unity-mono-smoke", "Unity Mono Smoke", "1.0.0")]
public sealed class UnityMonoSmokePlugin : IInsiderPlugin
{
    public const string Marker = "INSIDER_UNITY_MONO_SMOKE_PLUGIN_LOADED";

    private string? _insiderDirectory;
    private readonly int _baseValue = 5;
    private readonly object _gameHookSync = new object();
    private readonly List<Assembly> _hookedGameAssemblies = new List<Assembly>();
    private IInsiderContext? _context;

    public void Load(IInsiderContext context)
    {
        _context = context;
        _insiderDirectory = context.InsiderDirectory;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryInstallGameHook(assembly);
        }

        var target = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(HookTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unity smoke hook target was not found.");
        _ = context.Hooks.Detour(target, (Func<int>)HookReplacement);

        var instanceTarget = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(InstanceHookTarget),
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unity smoke instance hook target was not found.");
        _ = context.Hooks.Detour(instanceTarget, (InstanceReplacement)InstanceHookReplacement);

        var hookedValue = HookTarget();
        if (hookedValue != 42)
        {
            throw new InvalidOperationException($"Unity smoke detour returned {hookedValue}; expected 42.");
        }

        var instanceHookedValue = InstanceHookTarget(2);
        if (instanceHookedValue != 42)
        {
            throw new InvalidOperationException($"Unity smoke instance detour returned {instanceHookedValue}; expected 42.");
        }

        context.Logger.Info(Marker);
        File.WriteAllText(
            Path.Combine(context.InsiderDirectory, "unity-smoke-plugin-loaded.txt"),
            $"Backend={context.Runtime.Backend}{Environment.NewLine}" +
            $"Architecture={context.Runtime.Architecture}{Environment.NewLine}" +
            $"HookedValue={hookedValue}{Environment.NewLine}" +
            $"InstanceHookedValue={instanceHookedValue}{Environment.NewLine}" +
            $"GameDirectory={context.GameDirectory}{Environment.NewLine}");
    }

    public void Unload()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

        if (_insiderDirectory is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(_insiderDirectory, "unity-smoke-plugin-unloaded.txt"),
            $"unloaded{Environment.NewLine}" +
            $"HookedValue={HookTarget()}{Environment.NewLine}" +
            $"InstanceHookedValue={InstanceHookTarget(2)}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int HookTarget()
    {
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int HookReplacement()
    {
        return 42;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int InstanceHookTarget(int value)
    {
        return _baseValue + value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InstanceHookReplacement(
        InstanceOriginal original,
        UnityMonoSmokePlugin self,
        int value)
    {
        return original(self, value) * 6;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GameHookReplacement(GameOriginal original, int value)
    {
        return original(value) * 6;
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs eventArgs)
    {
        try
        {
            TryInstallGameHook(eventArgs.LoadedAssembly);
        }
        catch (Exception exception)
        {
            _context?.Logger.Error("Could not install the Unity smoke game hook.", exception);
        }
    }

    private void TryInstallGameHook(Assembly assembly)
    {
        if (!string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
        {
            return;
        }

        var context = _context
            ?? throw new InvalidOperationException("Unity smoke plugin context is unavailable.");

        lock (_gameHookSync)
        {
            if (_hookedGameAssemblies.Contains(assembly))
            {
                return;
            }

            var gameType = assembly.GetType("Insider.UnityMonoSmoke.SmokePlayer", throwOnError: true)
                ?? throw new InvalidOperationException("Unity smoke game type was not found.");
            var gameTarget = gameType.GetMethod(
                "CalculateHookValue",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Unity smoke game hook target was not found.");
            _ = context.Hooks.Detour(gameTarget, (GameReplacement)GameHookReplacement);

            var gameHookedValue = gameTarget.Invoke(null, new object[] { 2 });
            if (!Equals(gameHookedValue, 42))
            {
                throw new InvalidOperationException($"Unity smoke game detour returned {gameHookedValue}; expected 42.");
            }

            _hookedGameAssemblies.Add(assembly);
            File.AppendAllText(
                Path.Combine(context.InsiderDirectory, "unity-smoke-game-hooked.txt"),
                $"GameHookAssembly={assembly.GetName().Name}{Environment.NewLine}" +
                $"GameHookedValue={gameHookedValue}{Environment.NewLine}");
            context.Logger.Info("INSIDER_UNITY_MONO_SMOKE_GAME_HOOK_INSTALLED");
        }
    }

    private delegate int InstanceOriginal(UnityMonoSmokePlugin self, int value);

    private delegate int InstanceReplacement(
        InstanceOriginal original,
        UnityMonoSmokePlugin self,
        int value);

    private delegate int GameOriginal(int value);

    private delegate int GameReplacement(GameOriginal original, int value);
}

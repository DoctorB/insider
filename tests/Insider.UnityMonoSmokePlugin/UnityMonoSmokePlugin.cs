using System;
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

    public void Load(IInsiderContext context)
    {
        _insiderDirectory = context.InsiderDirectory;
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

    private delegate int InstanceOriginal(UnityMonoSmokePlugin self, int value);

    private delegate int InstanceReplacement(
        InstanceOriginal original,
        UnityMonoSmokePlugin self,
        int value);
}

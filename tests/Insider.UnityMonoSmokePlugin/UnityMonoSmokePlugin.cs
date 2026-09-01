using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Insider;

namespace Insider.UnityMonoSmokePlugin;

[InsiderPlugin("dev.insider.tests.unity-mono-smoke", "Unity Mono Smoke", "1.0.0")]
public sealed class UnityMonoSmokePlugin : IInsiderPlugin
{
    public const string Marker = "INSIDER_UNITY_MONO_SMOKE_PLUGIN_LOADED";
    private const int GameHookRemovalDelayMilliseconds = 2000;

    private string? _insiderDirectory;
    private readonly int _baseValue = 5;
    private readonly object _gameHookSync = new object();
    private readonly List<Assembly> _hookedGameAssemblies = new List<Assembly>();
    private readonly List<IDisposable> _gameHookHandles = new List<IDisposable>();
    private readonly List<MethodInfo> _gameHookTargets = new List<MethodInfo>();
    private static int _refReturnOriginalValue = 7;
    private static int _refReturnReplacementValue = 42;
    private IInsiderContext? _context;
    private Timer? _gameHookRemovalTimer;

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

        var refOutTarget = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(RefOutHookTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unity smoke ref/out hook target was not found.");
        _ = context.Hooks.Detour(refOutTarget, (RefOutReplacement)RefOutHookReplacement);

        var inParameterTarget = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(InParameterHookTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unity smoke in-parameter hook target was not found.");
        _ = context.Hooks.Detour(
            inParameterTarget,
            (InParameterReplacement)InParameterHookReplacement);

        var refReturnTarget = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(RefReturnHookTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unity smoke ref-return hook target was not found.");
        _ = context.Hooks.Detour(refReturnTarget, (RefReturnReplacement)RefReturnHookReplacement);

        var instanceTarget = typeof(UnityMonoSmokePlugin).GetMethod(
            nameof(InstanceHookTarget),
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unity smoke instance hook target was not found.");
        _ = context.Hooks.Detour(instanceTarget, (InstanceReplacement)InstanceHookReplacement);

        var virtualBaseTarget = typeof(VirtualBaseHookTarget).GetMethod(
            nameof(VirtualBaseHookTarget.Calculate),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("Unity smoke virtual base hook target was not found.");
        _ = context.Hooks.Detour(virtualBaseTarget, (VirtualBaseReplacement)VirtualBaseHookReplacement);

        var virtualOverrideTarget = typeof(VirtualDerivedHookTarget).GetMethod(
            nameof(VirtualDerivedHookTarget.Calculate),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("Unity smoke virtual override hook target was not found.");
        _ = context.Hooks.Detour(
            virtualOverrideTarget,
            (VirtualDerivedReplacement)VirtualDerivedHookReplacement);

        var valueTypeTarget = typeof(ValueTypeHookTarget).GetMethod(
            nameof(ValueTypeHookTarget.Add),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unity smoke value-type hook target was not found.");
        _ = context.Hooks.Detour(valueTypeTarget, (ValueTypeReplacement)ValueTypeHookReplacement);

        var hookedValue = HookTarget();
        if (hookedValue != 42)
        {
            throw new InvalidOperationException($"Unity smoke detour returned {hookedValue}; expected 42.");
        }

        var refOutValue = 2;
        var refOutSucceeded = RefOutHookTarget(ref refOutValue, out var refOutOutput);
        if (!refOutSucceeded || refOutValue != 8 || refOutOutput != 26)
        {
            throw new InvalidOperationException(
                $"Unity smoke ref/out detour returned {refOutSucceeded}, {refOutValue}, {refOutOutput}; expected true, 8, 26.");
        }

        var inParameterValue = 2;
        var inParameterHookedValue = InParameterHookTarget(in inParameterValue);
        if (inParameterHookedValue != 14)
        {
            throw new InvalidOperationException(
                $"Unity smoke in-parameter detour returned {inParameterHookedValue}; expected 14.");
        }

        ref var refReturn = ref RefReturnHookTarget();
        var refReturnHookedValue = refReturn;
        if (refReturnHookedValue != 42 || _refReturnOriginalValue != 12)
        {
            throw new InvalidOperationException(
                $"Unity smoke ref-return detour returned {refReturnHookedValue} with original state {_refReturnOriginalValue}; expected 42 and 12.");
        }

        refReturn = 50;
        if (_refReturnReplacementValue != 50)
        {
            throw new InvalidOperationException(
                $"Unity smoke ref-return storage contained {_refReturnReplacementValue}; expected 50.");
        }

        var instanceHookedValue = InstanceHookTarget(2);
        if (instanceHookedValue != 42)
        {
            throw new InvalidOperationException($"Unity smoke instance detour returned {instanceHookedValue}; expected 42.");
        }

        var virtualBaseHookedValue = new VirtualBaseHookTarget().Calculate(2);
        VirtualBaseHookTarget virtualDerivedInstance = new VirtualDerivedHookTarget();
        var virtualOverrideHookedValue = virtualDerivedInstance.Calculate(2);
        if (virtualBaseHookedValue != 14 || virtualOverrideHookedValue != 30)
        {
            throw new InvalidOperationException(
                $"Unity smoke virtual detours returned {virtualBaseHookedValue} and {virtualOverrideHookedValue}; expected 14 and 30.");
        }

        var valueTypeInstance = new ValueTypeHookTarget(5);
        var valueTypeHookedValue = valueTypeInstance.Add(2);
        if (valueTypeHookedValue != 42 || valueTypeInstance.Value != 7)
        {
            throw new InvalidOperationException(
                $"Unity smoke value-type detour returned {valueTypeHookedValue} with state {valueTypeInstance.Value}; expected 42 and 7.");
        }

        context.Logger.Info(Marker);
        File.WriteAllText(
            Path.Combine(context.InsiderDirectory, "unity-smoke-plugin-loaded.txt"),
            $"Backend={context.Runtime.Backend}{Environment.NewLine}" +
            $"Architecture={context.Runtime.Architecture}{Environment.NewLine}" +
            $"HookedValue={hookedValue}{Environment.NewLine}" +
            $"RefOutValue={refOutValue}{Environment.NewLine}" +
            $"RefOutOutput={refOutOutput}{Environment.NewLine}" +
            $"InParameterHookedValue={inParameterHookedValue}{Environment.NewLine}" +
            $"RefReturnHookedValue={refReturnHookedValue}{Environment.NewLine}" +
            $"RefReturnOriginalValue={_refReturnOriginalValue}{Environment.NewLine}" +
            $"RefReturnReplacementValue={_refReturnReplacementValue}{Environment.NewLine}" +
            $"InstanceHookedValue={instanceHookedValue}{Environment.NewLine}" +
            $"VirtualBaseHookedValue={virtualBaseHookedValue}{Environment.NewLine}" +
            $"VirtualOverrideHookedValue={virtualOverrideHookedValue}{Environment.NewLine}" +
            $"ValueTypeHookedValue={valueTypeHookedValue}{Environment.NewLine}" +
            $"ValueTypeState={valueTypeInstance.Value}{Environment.NewLine}" +
            $"GameDirectory={context.GameDirectory}{Environment.NewLine}");
    }

    public void Unload()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        lock (_gameHookSync)
        {
            _gameHookRemovalTimer?.Dispose();
            _gameHookRemovalTimer = null;
        }

        if (_insiderDirectory is null)
        {
            return;
        }

        var valueTypeInstance = new ValueTypeHookTarget(5);
        var refOutValue = 2;
        _ = RefOutHookTarget(ref refOutValue, out var refOutOutput);
        var inParameterValue = 2;
        var inParameterHookedValue = InParameterHookTarget(in inParameterValue);
        ref var refReturn = ref RefReturnHookTarget();
        var refReturnHookedValue = refReturn;
        var virtualBaseHookedValue = new VirtualBaseHookTarget().Calculate(2);
        VirtualBaseHookTarget virtualDerivedInstance = new VirtualDerivedHookTarget();
        File.WriteAllText(
            Path.Combine(_insiderDirectory, "unity-smoke-plugin-unloaded.txt"),
            $"unloaded{Environment.NewLine}" +
            $"HookedValue={HookTarget()}{Environment.NewLine}" +
            $"RefOutValue={refOutValue}{Environment.NewLine}" +
            $"RefOutOutput={refOutOutput}{Environment.NewLine}" +
            $"InParameterHookedValue={inParameterHookedValue}{Environment.NewLine}" +
            $"RefReturnHookedValue={refReturnHookedValue}{Environment.NewLine}" +
            $"RefReturnOriginalValue={_refReturnOriginalValue}{Environment.NewLine}" +
            $"RefReturnReplacementValue={_refReturnReplacementValue}{Environment.NewLine}" +
            $"InstanceHookedValue={InstanceHookTarget(2)}{Environment.NewLine}" +
            $"VirtualBaseHookedValue={virtualBaseHookedValue}{Environment.NewLine}" +
            $"VirtualOverrideHookedValue={virtualDerivedInstance.Calculate(2)}{Environment.NewLine}" +
            $"ValueTypeHookedValue={valueTypeInstance.Add(2)}{Environment.NewLine}" +
            $"ValueTypeState={valueTypeInstance.Value}");
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
    private static bool RefOutHookTarget(ref int value, out int output)
    {
        value += 5;
        output = value * 2;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool RefOutHookReplacement(
        RefOutOriginal original,
        ref int value,
        out int output)
    {
        value++;
        var result = original(ref value, out output);
        output += 10;
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InParameterHookTarget(in int value)
    {
        return value + 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InParameterHookReplacement(InParameterOriginal original, in int value)
    {
        return original(in value) * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ref int RefReturnHookTarget()
    {
        return ref _refReturnOriginalValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ref int RefReturnHookReplacement(RefReturnOriginal original)
    {
        ref var originalValue = ref original();
        originalValue += 5;
        return ref _refReturnReplacementValue;
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
    private static int VirtualBaseHookReplacement(
        VirtualBaseOriginal original,
        VirtualBaseHookTarget self,
        int value)
    {
        return original(self, value) * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int VirtualDerivedHookReplacement(
        VirtualDerivedOriginal original,
        VirtualDerivedHookTarget self,
        int value)
    {
        return original(self, value) + 20;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ValueTypeHookReplacement(
        ValueTypeOriginal original,
        ref ValueTypeHookTarget self,
        int value)
    {
        return original(ref self, value) * 6;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FirstGameHookReplacement(GameOriginal original, int value)
    {
        return original(value) + 14;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SecondGameHookReplacement(GameOriginal original, int value)
    {
        return original(value) + 21;
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
            var firstHook = context.Hooks.Detour(gameTarget, (GameReplacement)FirstGameHookReplacement);
            IDisposable? secondHook = null;
            try
            {
                secondHook = context.Hooks.Detour(gameTarget, (GameReplacement)SecondGameHookReplacement);

                var gameHookedValue = gameTarget.Invoke(null, new object[] { 2 });
                if (!Equals(gameHookedValue, 42))
                {
                    throw new InvalidOperationException($"Unity smoke game detour returned {gameHookedValue}; expected 42.");
                }

                _gameHookHandles.Add(firstHook);
                _gameHookHandles.Add(secondHook);
                _gameHookTargets.Add(gameTarget);
                _hookedGameAssemblies.Add(assembly);
                File.AppendAllText(
                    Path.Combine(context.InsiderDirectory, "unity-smoke-game-hooked.txt"),
                    $"GameHookAssembly={assembly.GetName().Name}{Environment.NewLine}" +
                    $"GameHookCount=2{Environment.NewLine}" +
                    $"GameHookedValue={gameHookedValue}{Environment.NewLine}");
                context.Logger.Info("INSIDER_UNITY_MONO_SMOKE_GAME_HOOK_INSTALLED");

                if (_gameHookRemovalTimer is null)
                {
                    _gameHookRemovalTimer = new Timer(RemoveGameHooks, null, GameHookRemovalDelayMilliseconds, Timeout.Infinite);
                }
                else
                {
                    _gameHookRemovalTimer.Change(GameHookRemovalDelayMilliseconds, Timeout.Infinite);
                }
            }
            catch
            {
                secondHook?.Dispose();
                firstHook.Dispose();
                throw;
            }
        }
    }

    private void RemoveGameHooks(object? state)
    {
        try
        {
            IDisposable[] handles;
            MethodInfo[] targets;
            Timer? timer;
            lock (_gameHookSync)
            {
                handles = _gameHookHandles.ToArray();
                targets = _gameHookTargets.ToArray();
                _gameHookHandles.Clear();
                _gameHookTargets.Clear();
                timer = _gameHookRemovalTimer;
                _gameHookRemovalTimer = null;
            }

            timer?.Dispose();
            for (var index = handles.Length - 1; index >= 0; index--)
            {
                handles[index].Dispose();
            }

            var context = _context
                ?? throw new InvalidOperationException("Unity smoke plugin context is unavailable.");
            foreach (var target in targets)
            {
                var restoredValue = target.Invoke(null, new object[] { 2 });
                if (!Equals(restoredValue, 7))
                {
                    throw new InvalidOperationException(
                        $"Unity smoke game hook removal returned {restoredValue}; expected 7.");
                }

                File.AppendAllText(
                    Path.Combine(context.InsiderDirectory, "unity-smoke-game-hooks-removed.txt"),
                    $"GameHookAssembly={target.DeclaringType?.Assembly.GetName().Name}{Environment.NewLine}" +
                    $"GameHookCount=0{Environment.NewLine}" +
                    $"GameRestoredValue={restoredValue}{Environment.NewLine}");
            }

            context.Logger.Info("INSIDER_UNITY_MONO_SMOKE_GAME_HOOKS_REMOVED");
        }
        catch (Exception exception)
        {
            _context?.Logger.Error("Could not remove the Unity smoke game hooks.", exception);
        }
    }

    private delegate int InstanceOriginal(UnityMonoSmokePlugin self, int value);

    private delegate bool RefOutOriginal(ref int value, out int output);

    private delegate bool RefOutReplacement(
        RefOutOriginal original,
        ref int value,
        out int output);

    private delegate int InParameterOriginal(in int value);

    private delegate int InParameterReplacement(InParameterOriginal original, in int value);

    private delegate ref int RefReturnOriginal();

    private delegate ref int RefReturnReplacement(RefReturnOriginal original);

    private delegate int InstanceReplacement(
        InstanceOriginal original,
        UnityMonoSmokePlugin self,
        int value);

    private delegate int VirtualBaseOriginal(VirtualBaseHookTarget self, int value);

    private delegate int VirtualBaseReplacement(
        VirtualBaseOriginal original,
        VirtualBaseHookTarget self,
        int value);

    private delegate int VirtualDerivedOriginal(VirtualDerivedHookTarget self, int value);

    private delegate int VirtualDerivedReplacement(
        VirtualDerivedOriginal original,
        VirtualDerivedHookTarget self,
        int value);

    private delegate int ValueTypeOriginal(ref ValueTypeHookTarget self, int value);

    private delegate int ValueTypeReplacement(
        ValueTypeOriginal original,
        ref ValueTypeHookTarget self,
        int value);

    private delegate int GameOriginal(int value);

    private delegate int GameReplacement(GameOriginal original, int value);

    private class VirtualBaseHookTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual int Calculate(int value)
        {
            return value + 5;
        }
    }

    private sealed class VirtualDerivedHookTarget : VirtualBaseHookTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Calculate(int value)
        {
            return value + 8;
        }
    }

    private struct ValueTypeHookTarget
    {
        private int _value;

        public ValueTypeHookTarget(int value)
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
    }
}

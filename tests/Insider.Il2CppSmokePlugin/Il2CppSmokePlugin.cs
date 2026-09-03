using System;
using System.IO;
using System.Runtime.InteropServices;
using Insider;

namespace Insider.Il2CppSmokePlugin;

[InsiderPlugin("dev.insider.tests.il2cpp-smoke", "IL2CPP Smoke", "1.0.0")]
public sealed class Il2CppSmokePlugin : IInsiderPlugin
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ScoreMethod(IntPtr self, IntPtr methodInfo);

    public void Load(IInsiderContext context)
    {
        var runtime = context.Il2Cpp
            ?? throw new InvalidOperationException("The IL2CPP runtime bridge was not supplied.");
        if (context.Runtime.Backend != InsiderRuntimeBackend.UnityIl2Cpp ||
            !context.Runtime.SupportsNativeDetours ||
            context.Runtime.SupportsManagedDetours ||
            context.Runtime.SupportsIlHooks ||
            context.Runtime.SupportsMainThread)
        {
            throw new InvalidOperationException("The IL2CPP capability report is inconsistent.");
        }

        _ = runtime.ResolveExport("il2cpp_domain_get");
        var methodInfo = runtime.ResolveMethodInfo(
            "Assembly-CSharp",
            "Insider.Fixture",
            "Score",
            "GetValue",
            parameterCount: 0);
        var target = runtime.ResolveMethod(
            "Assembly-CSharp.dll",
            "Insider.Fixture",
            "Score",
            "GetValue",
            parameterCount: 0);
        var invoke = Marshal.GetDelegateForFunctionPointer<ScoreMethod>(target);

        using var hook = context.Hooks.DetourNative(target, (ScoreMethod)ReplaceScore);
        var hooked = invoke(IntPtr.Zero, methodInfo);
        hook.Dispose();
        var restored = invoke(IntPtr.Zero, methodInfo);

        File.WriteAllLines(
            Path.Combine(context.InsiderDirectory, "il2cpp-smoke.txt"),
            new[]
            {
                $"Backend={context.Runtime.Backend}",
                $"Hooked={hooked}",
                $"Restored={restored}",
            });
    }

    public void Unload()
    {
    }

    private static int ReplaceScore(IntPtr self, IntPtr methodInfo)
    {
        _ = self;
        _ = methodInfo;
        return 42;
    }
}

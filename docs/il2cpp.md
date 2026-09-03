# Essential IL2CPP backend

Insider can load managed plugins in complete Windows x64 Unity IL2CPP players.
This is an intentionally low-level first backend: it supplies runtime metadata
resolution and native detours without pretending that compiled IL2CPP game code
is managed IL.

## What is available

On IL2CPP, the plugin context reports:

| API | State |
| --- | --- |
| Plugin discovery, dependencies, lifecycle, logs, and owned directories | Available |
| `context.Il2Cpp` | Available and non-null |
| `context.Hooks.DetourNative` | Available |
| `context.Hooks.Detour(MethodBase, ...)` | Not supported for game methods |
| `context.Hooks.ModifyIl` | Not supported |
| `context.MainThread` and `RegisterUpdate` | Not supported yet |
| Automatic Unity/game managed proxies | Not included |

Check capabilities instead of inferring them from an Insider version:

```csharp
if (context.Runtime.Backend != InsiderRuntimeBackend.UnityIl2Cpp ||
    !context.Runtime.SupportsNativeDetours ||
    context.Il2Cpp is null)
{
    context.Logger.Info("This plugin requires the IL2CPP native backend.");
    return;
}
```

## Resolve runtime metadata

`IInsiderIl2CppRuntime` exposes three operations:

```csharp
IntPtr export = context.Il2Cpp.ResolveExport("il2cpp_domain_get");

IntPtr methodInfo = context.Il2Cpp.ResolveMethodInfo(
    "Assembly-CSharp",
    "Example.Gameplay",
    "PlayerScore",
    "get_Score",
    parameterCount: 0);

IntPtr nativeMethod = context.Il2Cpp.ResolveMethod(
    "Assembly-CSharp",
    "Example.Gameplay",
    "PlayerScore",
    "get_Score",
    parameterCount: 0);
```

Assembly names accept either `Assembly-CSharp` or `Assembly-CSharp.dll`.
Namespace may be empty. Resolution uses the IL2CPP name and parameter-count API;
overloads with the same name and count are ambiguous and should not be targeted
without game-specific verification. `ResolveMethodInfo` returns an opaque
native pointer. Do not write to it.

## Install a native method detour

The replacement must match the generated native ABI exactly. A typical IL2CPP
instance method has a native `self` pointer, its declared arguments, and a
trailing `MethodInfo` pointer. Verify the actual generated signature for the
target game and Unity version before using it.

```csharp
using System;
using System.Runtime.InteropServices;
using Insider;

[InsiderPlugin("com.example.il2cpp-score", "IL2CPP Score", "0.1.0")]
public sealed class ScorePlugin : IInsiderPlugin
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetScore(IntPtr self, IntPtr methodInfo);

    public void Load(IInsiderContext context)
    {
        if (context.Il2Cpp is null || !context.Runtime.SupportsNativeDetours)
        {
            throw new NotSupportedException("This plugin requires Unity IL2CPP native detours.");
        }

        var target = context.Il2Cpp.ResolveMethod(
            "Assembly-CSharp",
            "Example.Gameplay",
            "PlayerScore",
            "get_Score",
            parameterCount: 0);

        _ = context.Hooks.DetourNative(target, (GetScore)ReplaceScore);
    }

    public void Unload()
    {
    }

    private static int ReplaceScore(IntPtr self, IntPtr methodInfo)
    {
        _ = self;
        _ = methodInfo;
        return 9001;
    }
}
```

The returned handle can remove the detour early. If the plugin leaves it active,
Insider removes it automatically after `Unload()` or a failed `Load()`, using
the same ownership rules as other hooks.

## Safety rules

- Treat every resolved address as process-local and valid only for that launch.
- Keep the replacement delegate alive by keeping the returned handle owned by
  the plugin context; do not create a second unmanaged function pointer yourself.
- Match return type, instance/static shape, argument order, widths, pointer
  indirection, and hidden IL2CPP arguments exactly.
- Do not call Unity APIs from `Load()`. IL2CPP main-thread dispatch is not
  available yet, and `context.MainThread` fails clearly instead of guessing.
- Expect game updates, stripping, generic sharing, thunks, and changed Unity
  metadata layouts to invalidate targets.
- Use `insider diagnose <game.exe>` before launch, then inspect both
  `Insider/logs/native.log` and `Insider/logs/insider.log` after launch.

An incorrect native signature can corrupt memory or terminate the game. The
essential backend is suitable for carefully versioned plugins, not portable
reflection-style modding yet.

## Runtime and packaging

The package installs its private runtime under `Insider/runtime/win-x64`. It is
loader-owned, hashed in `Insider/install.json`, and removed by uninstall unless
modified. Plugins must not add files to that directory.

The private runtime exists only for the IL2CPP bootstrap; the CLI remains a
framework-dependent .NET 10 application. Insider does not use BepInEx and does
not require a machine-wide runtime inside the game process.

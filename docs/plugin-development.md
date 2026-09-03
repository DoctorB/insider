# Plugin development

Insider's plugin API targets .NET Standard 2.0 so the same contract can run on
the modern Unity Mono profiles in the initial compatibility scope.

## Create a plugin

Reference `Insider.Abstractions`, implement `IInsiderPlugin`, and add one
`InsiderPluginAttribute` with a stable, globally unique identifier:

```csharp
using Insider;

[InsiderPlugin("com.example.my-plugin", "My Plugin", "0.1.0")]
public sealed class MyPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        context.Logger.Info("My Plugin loaded.");
    }

    public void Unload()
    {
    }
}
```

Do not redistribute `Insider.Abstractions.dll` with the plugin. The loader ships
and owns the contract assembly.

## Plugin-owned directories

`IInsiderContext` exposes the paths assigned to the current plugin:

| Property | Purpose | Ownership |
| --- | --- | --- |
| `PluginDirectory` | Entry assembly and bundled read-only assets | Installation layout; it may be shared by plugin types from the same assembly |
| `ConfigDirectory` | Persistent user-editable settings | The plugin owns everything below this directory |
| `DataDirectory` | Persistent caches, state, or generated content | The plugin owns everything below this directory |

`ConfigDirectory` and `DataDirectory` are isolated by the plugin's
case-insensitive stable ID and exist before `Load()` is called. They are not
removed when the plugin unloads or when Insider is uninstalled. A conventional
ID such as `com.example.my-plugin` produces this layout:

```text
Insider/
  plugins/MyPlugin.dll
  config/com.example.my-plugin/
  data/com.example.my-plugin/
```

IDs that are not safe portable directory names receive a deterministic safe
segment. Always use the paths from `context`; do not derive them from the plugin
ID or write outside them. `PluginDirectory` identifies the actual directory
that supplied the entry assembly, which can differ from the root plugin
directory and can be shared. Keep persistent files in `ConfigDirectory` or
`DataDirectory`, not next to the assembly.

Insider deliberately provides paths and ownership only. It has no settings
schema, serializer, cache API, or automatic migration. A plugin can choose the
simplest format that fits its needs:

```csharp
using System.IO;

public void Load(IInsiderContext context)
{
    var preferencesPath = Path.Combine(context.ConfigDirectory, "preferences.txt");
    if (!File.Exists(preferencesPath))
    {
        File.WriteAllText(preferencesPath, "show-overlay=true");
    }

    var cachePath = Path.Combine(context.DataDirectory, "last-session.txt");
    File.WriteAllText(cachePath, "loaded");
}
```

## Plugin dependencies

Declare a required dependency on another plugin by its stable ID:

```csharp
[InsiderPluginDependency("com.example.foundation")]
```

To require a minimum version, pass it as the second argument:

```csharp
[InsiderPluginDependency("com.example.foundation", "1.2.0")]
```

For an integration that can be absent, declare an optional dependency:

```csharp
[InsiderPluginDependency("com.example.integration", "1.2.0", optional: true)]
```

Insider discovers all plugin types before activation and loads required plugins
first. A missing required ID, duplicate plugin ID, repeated dependency
declaration, or required dependency cycle prevents affected plugins from
running. If a required plugin throws during `Load()`, its dependants are skipped.

Optional dependencies are preferred earlier in the order when present. Their
absence or failure does not block the declaring plugin, and optional cycles are
broken deterministically. Declared dependencies are exposed through
`PluginDescriptor.Dependencies` for diagnostics.

## Disabling plugins

Create `Insider/config/disabled-plugins.txt` to keep a plugin installed while
preventing its activation. Add one stable plugin ID per line:

```text
# Waiting for a compatible game update
com.example.my-plugin
com.example.experimental
```

Insider trims each line, ignores empty lines and lines beginning with `#`, and
compares IDs without case sensitivity. Duplicate entries have no additional
effect. The file is optional and is read once during bootstrap; changing it
requires restarting the game.

The packaged CLI manages the same file by stable plugin ID:

```powershell
dotnet insider.dll plugins disable "C:\Games\Example\Example.exe" com.example.my-plugin
dotnet insider.dll plugins disabled "C:\Games\Example\Example.exe"
dotnet insider.dll plugins enable "C:\Games\Example\Example.exe" com.example.my-plugin
```

`disable` and `enable` are idempotent. They preserve comments, blank lines, and
unrelated entries; `enable` removes every case-insensitive occurrence of the
requested ID. `disabled` prints a de-duplicated, case-insensitively sorted view.
The commands require an Insider installation but remain available when its
status is damaged, so a problematic plugin can still be disabled before the
installation is repaired. A changed list affects only the next game start.

A disabled plugin is still discovered so Insider can read its metadata, but no
instance is created and `Load()` is not called. It is logged as skipped and does
not count as a load failure. A plugin with a required dependency on a disabled
ID fails before activation with a `(disabled)` diagnostic. An optional
dependency on that ID does not block activation.

Disabling does not repair an unreadable or structurally invalid assembly because
metadata discovery necessarily happens first. Remove such a DLL from
`Insider/plugins` when discovery itself fails. Insider preserves both the
`config` and `data` directory trees during uninstall. There is deliberately no
hot reload, wildcard, per-file switch, or separate configuration language.

## Version policy

Versions use exactly three non-negative integers: `MAJOR.MINOR.PATCH`. Leading
zeroes, prerelease labels, build metadata, wildcards, and range expressions are
not accepted. A dependency may declare one minimum version or no version at all.

This restricted model is intentional: comparison stays obvious and predictable.
A required dependency below the minimum blocks only its dependants. An optional
dependency below the minimum is treated as unavailable.

## Logging

Use `context.Logger` rather than writing directly to the console or Insider's log
file. Insider automatically prefixes every plugin message with its declared ID:

```text
2026-08-31T12:00:00.0000000Z [Information] [com.example.my-plugin] My Plugin loaded.
```

The prefix is added by the loader; plugins should not add it themselves.

## Managed detours

The hooking API applies a managed method or instance-constructor detour from a
reflected `MethodBase` and a compatible replacement delegate. This
instance-method example preserves the original behavior and changes its result:

The complete signature reference, lifecycle rules, chain behavior, Unity
assembly-loading pattern, and examples are maintained in
[hooking.md](hooking.md).

```csharp
using System;
using System.Reflection;

private delegate int ComputeOriginal(TargetType self, int value);
private delegate int ComputeHook(
    ComputeOriginal original,
    TargetType self,
    int value);

private IDisposable? _detour;

public void Load(IInsiderContext context)
{
    var target = typeof(TargetType).GetMethod(
        "Compute",
        BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Target method not found.");

    _detour = context.Hooks.Detour(target, (ComputeHook)Replacement);
}

private static int Replacement(
    ComputeOriginal original,
    TargetType self,
    int value)
{
    return original(self, value) * 2;
}
```

The detour is active as soon as `Detour` returns. Dispose the returned handle to
remove it early. Insider also owns every hook handle created through a plugin
context and removes remaining detours and IL hooks after that plugin's
`Unload()` callback or after a failed `Load()`.

Signatures are exact. A direct replacement receives the target arguments. An
instance-method or constructor replacement receives the declaring type as
`self` before those arguments. Constructors use a `void` replacement and
original-call delegate. Declared `ref`, `out`, and `in` parameters remain by
reference in both delegate signatures; the same applies to by-reference
returns. Virtual base methods and overrides use their exact declaring type as
`self` and must be reflected separately. To call the original behavior, prepend
a delegate with that same return type and parameter list, as in the example
above. Call this delegate only synchronously while the replacement is
executing; do not store it.

Value-type instance methods use `ref self`; both the replacement and its
original-call delegate must declare it exactly:

```csharp
private delegate int ApplyOriginal(ref TargetStruct self, int value);
private delegate int ApplyHook(
    ApplyOriginal original,
    ref TargetStruct self,
    int value);

private static int Replacement(
    ApplyOriginal original,
    ref TargetStruct self,
    int value)
{
    return original(ref self, value) * 2;
}
```

Passing the struct by value is rejected because mutations would target a copy.

If more than one detour targets the same method, that delegate advances to the
next detour and eventually the original method. Insider does not define
inter-plugin detour order yet. Disposing one handle removes only that detour;
cleanup after a failed plugin load does not remove detours owned by other
plugins.

Disposal is idempotent. If the runtime cannot remove a detour, the handle stays
retryable and the failure is reported as `InsiderHookException`; loader cleanup
continues with the plugin's other handles and aggregates any failures.

Hook the `MethodInfo` or `ConstructorInfo` from the assembly instance Unity
actually uses. Game assemblies such as `Assembly-CSharp` may load after Insider
plugins. Do not force an early private copy with `Assembly.Load`; observe
`AppDomain.AssemblyLoad`, install the hook when the requested assembly arrives,
and unsubscribe during `Unload()`. Hooks created through the saved plugin
context remain loader-owned.

Abstract methods, all generic methods, members declared on generic types,
multicast replacement delegates, variable-argument methods, static constructors,
and value-type constructors are rejected by `Detour`. HookGen, ordering controls,
and native hooks remain outside the Insider contract even when the underlying
backend offers related features.

## IL hooks

Use `context.Hooks.ModifyIl(target, manipulator)` for a precise edit inside a
method body. The manipulator receives MonoMod's `ILContext`, so plugin code can
use `ILCursor`, Cecil opcodes, labels, locals, and exception handlers:

```csharp
using Mono.Cecil.Cil;
using MonoMod.Cil;

private IDisposable? _ilHook;

private void InstallIlHook(IInsiderContext context, MethodInfo target)
{
    _ilHook = context.Hooks.ModifyIl(target, il =>
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(
            MoveType.Before,
            instruction => instruction.MatchLdcI4(7)))
        {
            throw new InvalidOperationException("Expected IL pattern not found.");
        }

        cursor.Remove();
        cursor.Emit(OpCodes.Ldc_I4, 42);
    });
}
```

Match enough surrounding instructions to make the location unambiguous, fail
closed when the game changes, and leave a valid evaluation stack. MonoMod may
run the callback again when the IL-hook chain changes, so it must be
deterministic and must not retain the supplied context, cursor, instructions, or
labels. Multiple IL hooks are independently removable, but Insider deliberately
does not expose their ordering.

`ModifyIl` rejects targets without readable managed IL, generic and vararg
targets, static constructors, and multicast manipulators. It can rewrite class
or value-type instance constructors when they expose IL. Apply and removal
failures use `InsiderHookException` and failed removal stays retryable under the
same plugin ownership rules as a detour.

The Insider package supplies the pinned `MonoMod.*` and `Mono.Cecil*` runtime
assemblies from `Insider/core`. The loader resolves these host-owned identities
for plugins and their transitive requests. Use the compile-time dependency
selected by `Insider.Abstractions`; do not copy those host assemblies into the
plugin or its dependency directory.
The complete IL examples and safety rules live in [hooking.md](hooking.md).

## Installation layout

Place plugin entry assemblies directly in `Insider/plugins`. Put their managed
dependencies in the shared `dependencies` subtree:

```text
Insider/
  plugins/
    MyPlugin.dll
    dependencies/
      Example.Library.dll
```

Only top-level DLLs are scanned for plugins. Dependency directories may be
nested for organization, but every managed DLL beneath `dependencies` is part
of one process-wide catalog. Native libraries may also live there and are
ignored by the managed catalog.

## Dependency rules

Insider builds one catalog from the top-level plugin DLLs, their shared
`dependencies` subtree, and the host-owned DLLs in `Insider/core`. It resolves
an exact assembly identity: name, version, culture, and public key token. The
following conditions stop the plugin scan before any plugin code runs:

- two dependency candidates have the same simple assembly name;
- two versions of the same assembly are present;
- a candidate conflicts with an assembly already loaded by the game;
- the plugin host is reused with a different plugin directory.

`Insider/core` is reserved for files installed and versioned by Insider. Do not
place plugin libraries there or redistribute host assemblies in the plugin
tree; either case can create a duplicate simple name and fail the scan.

A missing dependency fails only the affected plugin when the runtime requests
it. All resolution decisions and errors are recorded in
`Insider/logs/insider.log`.

If the game already contains the exact requested assembly identity, the runtime
may reuse that resident copy. A different loaded identity with the same simple
name is rejected; Insider cannot replace it safely.

Unity Mono uses a shared application domain. `Unload()` is a lifecycle callback,
not assembly unloading: managed assemblies remain resident until the game exits.
Plugin authors should therefore coordinate on common dependency versions and
avoid modifying global state they cannot restore. Insider removes context-owned
detours and IL hooks, but it cannot undo changes made through third-party
hooking APIs.
Insider reads managed images into memory and does not intentionally keep the
source DLL files open.

## Unity main thread

Plugins are loaded by Insider's early bootstrap thread. Do not call Unity APIs
directly from `Load()`. Post the smallest Unity-facing operation through the
plugin context instead:

```csharp
public void Load(IInsiderContext context)
{
    context.MainThread.Post(() =>
    {
        context.Logger.Info("Running on Unity's main thread.");
    });
}
```

`Post` is thread-safe and accepts work before the Unity pump is ready. Callbacks
run in FIFO order, and work posted while a queue snapshot is executing waits for
the next frame. `IsReady` reports whether Insider has observed the pump;
`IsCurrent` reports whether the caller is currently on its thread.

For short work that must run once per frame, use
`context.MainThread.RegisterUpdate(callback)`. It returns an `IDisposable` for
early removal; any remaining registrations are removed automatically after
`Unload()` or a failed `Load()`. Pending `Post` callbacks become inert at the
same boundary. `Unload()` itself does not run on Unity's main thread, and work
posted during it is invalidated when unloading completes. Keep all callbacks
short and release Unity resources before unload. The complete contract,
examples, ordering, and Unity-reference guidance are in
[main-thread.md](main-thread.md).

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
remove it early. Insider also owns every handle created through a plugin context
and removes remaining detours after that plugin's `Unload()` callback or after a
failed `Load()`.

Signatures are exact. A direct replacement receives the target arguments. An
instance-method or constructor replacement receives the declaring type as
`self` before those arguments. Constructors use a `void` replacement and
original-call delegate. To call the original behavior, prepend a delegate with
that same return type and parameter list, as in the example above. Call this
delegate only synchronously while the replacement is executing; do not store
it.

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

Hook the `MethodInfo` or `ConstructorInfo` from the assembly instance Unity
actually uses. Game assemblies such as `Assembly-CSharp` may load after Insider
plugins. Do not force an early private copy with `Assembly.Load`; observe
`AppDomain.AssemblyLoad`, install the detour when the requested assembly
arrives, and unsubscribe during `Unload()`. Detours created through the saved
plugin context remain loader-owned.

Abstract methods, open generic methods, variable-argument methods, static
constructors, and value-type constructors are rejected. IL rewriting, HookGen,
ordering controls, and native hooks remain outside the Insider contract even
when the underlying backend offers related features.

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

Insider resolves an exact assembly identity: name, version, culture, and public
key token. The following conditions stop the plugin scan before any plugin code
runs:

- two dependency candidates have the same simple assembly name;
- two versions of the same assembly are present;
- a candidate conflicts with an assembly already loaded by the game;
- the plugin host is reused with a different plugin directory.

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
detours, but it cannot undo changes made through third-party hooking APIs.
Insider reads managed images into memory and does not intentionally keep the
source DLL files open.

## Bootstrap timing

Plugins are currently loaded by the early bootstrap thread. Do not assume that
Unity APIs are initialized or that `Load()` runs on Unity's main thread. Main
thread scheduling will be introduced as a separate runtime integration layer.

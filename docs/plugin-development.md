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
avoid modifying global state they cannot restore. Insider reads managed images
into memory and does not intentionally keep the source DLL files open.

## Bootstrap timing

Plugins are currently loaded by the early bootstrap thread. Do not assume that
Unity APIs are initialized or that `Load()` runs on Unity's main thread. Main
thread scheduling will be introduced as a separate runtime integration layer.

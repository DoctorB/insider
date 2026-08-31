# Architecture

Insider separates process entry, plugin loading, runtime integration, and method
hooking so each layer can evolve without changing the plugin contract.

```text
Unity executable
  -> native bootstrap adapter
  -> Insider.Bootstrap
  -> runtime detection
  -> Insider.Loader
  -> plugin lifecycle
  -> hooking backend
```

## Components

### Insider.Abstractions

The public plugin contract. It contains metadata, lifecycle, logging, and runtime
information interfaces. It has no dependency on Unity, a mod loader, or a native
bootstrap.

### Insider.Loader

Discovers plugin assemblies, validates metadata and unique identifiers, creates
plugin instances, and owns their load/unload lifecycle. It must contain failures
to the affected plugin whenever possible.

### Insider.Bootstrap

The earliest managed entry point. It resolves the game and Insider directories,
creates diagnostics, detects the scripting backend, and starts the chainloader.
The exported `Doorstop.Entrypoint.Start()` method is an adapter boundary rather
than a dependency on a full mod loader.

### Insider.Cli

Out-of-process tooling for inspecting and eventually installing or removing
Insider. Installation is not implemented until the exact native bootstrap bundle
and provenance rules are defined.

### Runtime hooking backend

Not implemented yet. The first spike will target Unity Mono through
MonoMod.RuntimeDetour. The old v1 memory patcher is archived and must not be used
as the production backend.

## Design rules

- Plugin APIs must not expose Doorstop or another loader implementation.
- Compatibility is declared per scripting backend, operating system, and process
  architecture.
- Native crashes are tested in child processes, never inside the unit-test host.
- Early bootstrap code must assume Unity APIs are not initialized.
- A plugin failure must be logged with plugin identity and stage.
- Third-party binaries require recorded versions, hashes, sources, and licenses.

## Security boundary

There is no in-process security boundary between a plugin, Insider, and the game.
The loader validates metadata and lifecycle, not plugin intent.

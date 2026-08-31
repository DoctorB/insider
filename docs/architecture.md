# Architecture

Insider separates process entry, plugin loading, runtime integration, and method
hooking so each layer can evolve without changing the plugin contract.

```text
Unity executable
  -> Insider native version.dll proxy
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
The native loader invokes `Insider.Native.Entrypoint.Start()` through Mono's
embedding API. The exported `Doorstop.Entrypoint.Start()` method is retained as
a compatibility adapter rather than a dependency on a full mod loader.

### Insider.Bootstrap.Native

The Insider-owned Windows x64 process entry layer. It is installed as a local
`version.dll`, forwards the Windows version-information API to the operating
system, waits for Unity's existing Mono runtime, attaches its bootstrap thread,
and invokes the managed entry point. It does not ship or initialize a second
Mono runtime.

### Insider.Installation and Insider.Cli

Out-of-process tooling for inspecting, installing, verifying, and removing
Insider. Installations are described by a manifest containing SHA-256 hashes.
An existing root `version.dll` is preserved and restored; unknown core files are
never overwritten.

### Runtime hooking backend

Not implemented yet. The first spike will target Unity Mono through
MonoMod.RuntimeDetour. The old v1 memory patcher is archived and must not be used
as the production backend.

## Design rules

- Plugin APIs must not expose the native bootstrap or another loader implementation.
- Compatibility is declared per scripting backend, operating system, and process
  architecture.
- Native crashes are tested in child processes, never inside the unit-test host.
- Early bootstrap code must assume Unity APIs are not initialized.
- A plugin failure must be logged with plugin identity and stage.
- Third-party binaries require recorded versions, hashes, sources, and licenses.

## Security boundary

There is no in-process security boundary between a plugin, Insider, and the game.
The loader validates metadata and lifecycle, not plugin intent.

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

Discovers plugin assemblies, catalogs managed dependencies, validates metadata
and unique identifiers, creates plugin instances, and owns their load/unload
lifecycle. Requested dependency identities are resolved from the plugin tree;
an identical identity already resident in the game application domain may be
reused by the runtime. The loader rejects duplicate or conflicting assembly
names before plugin code runs because Unity Mono does not provide a safe
isolation boundary inside its shared application domain. Managed images are
read into memory before loading so the source DLLs are not held open by Insider
for the rest of the process.

Plugin types are discovered before activation. Required plugin-ID dependencies
form a directed graph that determines load order; missing nodes, duplicate IDs,
and required cycles fail before affected plugin code runs. Optional dependencies
are ordered first when possible but never create a hard graph edge.

Plugin versions use a deliberately small numeric `MAJOR.MINOR.PATCH` model.
Dependencies may specify one minimum version; arbitrary ranges are outside the
initial loader contract.

Each activated plugin receives a thin context wrapper whose logger prefixes
messages with the plugin ID. All other context values are delegated unchanged.

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
- Dependency resolution must be deterministic; ambiguous assembly identities
  fail closed before plugin discovery.
- Plugin activation must follow declared required dependencies, never incidental
  filesystem or reflection order.
- Third-party binaries require recorded versions, hashes, sources, and licenses.

## Security boundary

There is no in-process security boundary between a plugin, Insider, and the game.
The loader validates metadata and lifecycle, not plugin intent.

## Test boundary

The native fixture provides an Insider-owned module with the same seven Mono
embedding exports consumed by the bootstrap. Managed fixtures separately cover
real assembly discovery, exact dependency resolution, missing dependencies, and
version conflicts. These deterministic contract tests run in CI.

A separate local fixture builds a real Unity 2022.3 Windows x64 Mono player and
proves that the native proxy can enter the existing Mono domain, start the
managed loader, load one plugin, and unload it during process exit. It closes
the basic integration gap without turning one Unity version into a broad
support claim. See [testing.md](testing.md).

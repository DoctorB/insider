# Architecture

Insider separates process entry, plugin loading, runtime integration, main-thread
dispatch, and method hooking so each layer can evolve without changing the
plugin contract.

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

The public plugin contract. It contains metadata, lifecycle, logging, runtime
information, and the small managed-hook and main-thread service interfaces. It
has no dependency on Unity, a mod loader, or a native bootstrap. Managed detours
remain backend-neutral; the advanced `ModifyIl` operation deliberately exposes
MonoMod's `ILContext` because replacing that complete IL model with an
Insider-specific facade would add a large second instruction API.

### Insider.Loader

Discovers plugin assemblies, catalogs managed dependencies from the plugin tree
and the host-owned `Insider/core` directory, validates metadata
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

The bootstrap reads the optional `Insider/config/disabled-plugins.txt` file and
passes its normalized, case-insensitive ID set into directory loading. The loader
removes matching candidates after duplicate-ID validation and before dependency
validation or activation. Skipped plugins produce diagnostics but no failure
result; required dependants fail explicitly when their dependency is disabled.

Each activated plugin receives a thin context wrapper. It exposes the directory
containing the entry assembly plus isolated persistent configuration and data
directories derived safely from the case-insensitive plugin ID. The plugin owns
the files below its configuration and data directories; the loader creates the
directories before `Load()` but provides no configuration format or serializer.
The wrapper's logger prefixes messages with the plugin ID and its hooking
service tracks that plugin's detours and IL hooks. Remaining hooks are removed
in reverse creation order after `Unload()`, including cleanup after a failed
`Load()`. Its main-thread wrapper also makes pending callbacks inert and disposes
per-frame update registrations when the plugin context is disposed.

### Insider.Bootstrap

The earliest managed entry point. It resolves the game and Insider directories,
creates the plugin, configuration, data, and log roots, creates diagnostics,
detects the scripting backend, and starts the chainloader.
The native loader invokes `Insider.Native.Entrypoint.Start()` through Mono's
embedding API. The exported `Doorstop.Entrypoint.Start()` method is retained as
a compatibility adapter rather than a dependency on a full mod loader.

For Unity Mono, the bootstrap observes `UnityEngine.CoreModule` and installs a
loader-owned detour on `UnitySynchronizationContext.ExecuteTasks()` after the
managed resolver is active. Unity's original pump runs first; Insider then
drains one FIFO snapshot of posted plugin callbacks and invokes one ordered
snapshot of per-frame registrations on that same thread. Plugin loading remains
synchronous and deterministic on the bootstrap thread.

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
never overwritten. The CLI also lists, disables, and enables plugins by stable
ID through the existing `Insider/config/disabled-plugins.txt` format. Mutations
preserve user comments and unrelated lines, use a same-directory atomic replace,
and never attempt to change the state of a running game. Uninstall preserves
plugin assemblies, logs, and the plugin-owned configuration and data trees.

The read-only `diagnose` command composes runtime inspection, manifest
verification, the disable list, and an isolated metadata scan of the plugin
directory. The scan shares the public plugin metadata contract but never creates
plugin instances or calls lifecycle methods. It resolves required dependency
states, minimum versions, duplicates, and cycles before rendering one report;
the game executable is never started.

### Insider.Hooking

The first runtime backend implements `IInsiderHookService` through
MonoMod.RuntimeDetour. `Detour` creates direct managed method and
instance-constructor detours from a `MethodBase` and replacement `Delegate`.
`ModifyIl` rewrites a target with a readable managed body through a MonoMod
`ILContext`. Construction applies either hook immediately and disposal removes
only that handle's contribution.
Replacements use exact signatures, preserve declared by-reference parameters,
include `self` for reference-type instance members and `ref self` for value-type
instance methods, preserve managed by-reference returns, and may prepend an
original-call delegate to wrap existing behavior. Generic methods and members
of generic types fail closed because RuntimeDetour does not support generic
source hooks and Mono may share their generated code. Virtual base methods and
overrides are separate reflected implementations and separate hook targets.
Constructors use `void` signatures. Multiple detours can form a
continuation chain, but every handle remains independently owned and removable.
Multiple IL manipulators rebuild one target body in a backend-managed chain;
they must match semantic instruction patterns and remain deterministic because
the backend may invoke them again as that chain changes. IL hooks and detours
can share a target, but Insider does not define inter-plugin ordering.
The backend wraps application and removal failures in `InsiderHookException`;
successful disposal is idempotent, while failed disposal keeps the handle
tracked and retryable. IL hooks additionally require a readable method body and
place stack, branch, local, and exception-region correctness on the manipulator.
Static constructors, HookGen, hook ordering, and native detours are not exposed.
Value-type constructors remain unsupported by `Detour` but are valid
`ModifyIl` targets when reflection exposes their body.

The public signature, IL, and lifecycle rules are documented with working
patterns in the [runtime hooking guide](hooking.md).

The public scheduling contract, ownership rules, and Unity usage example are
documented in the [Unity main-thread guide](main-thread.md).

The old v1 memory patcher remains archived and is not used by the production
backend.

## Design rules

- Plugin APIs must not expose the native bootstrap or another loader implementation.
- Compatibility is declared per scripting backend, operating system, and process
  architecture.
- Native crashes are tested in child processes, never inside the unit-test host.
- Early bootstrap code must assume Unity APIs are not initialized.
- A plugin failure must be logged with plugin identity and stage.
- Dependency resolution must be deterministic; ambiguous assembly identities
  fail closed before plugin discovery.
- The managed resolver handles requests originating from catalogued plugin or
  core assemblies. Host runtime dependencies come only from `Insider/core`;
  plugins must not redistribute private copies.
- Plugin activation must follow declared required dependencies, never incidental
  filesystem or reflection order.
- Disabled plugin IDs must be applied before dependency ordering, without
  treating an intentional skip as a load failure.
- Plugins must use loader-assigned paths and keep persistent writes below their
  owned configuration or data directory.
- Unity-facing plugin work must be posted through the scoped main-thread service;
  `Load()` and `Unload()` are not main-thread callbacks.
- Work queued by an inactive or failed plugin must never execute later.
- Per-frame callbacks belong to their registering plugin and must be removed
  automatically when that plugin becomes inactive.
- Every detour or IL hook created through a plugin context belongs to that
  plugin and must be removed even when plugin load or unload fails.
- A failed removal must remain observable and retryable; it must not be marked
  complete or silently dropped from plugin ownership.
- Removing or rolling back one plugin's hooks must leave other owners' nodes
  in the same target chain intact.
- A hook must target the assembly instance used by Unity; late game assemblies
  are observed when loaded rather than forced into the application domain.
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
managed loader, load one plugin, dispatch a callback through Unity's real
synchronization pump, run and remove a per-frame callback, apply a managed
method detour, and unload the plugin during process exit. The dispatched
callbacks verify the Unity synchronization context and a live Unity API from
the main thread. The plugin also waits for Unity's real
`Assembly-CSharp` instance and detours a method that the player invokes
directly. It also rewrites a second game method through `ModifyIl`. The fixture
removes the detour chain and IL hook while the player remains active and
verifies that later direct calls return the original results. It
closes the basic integration gap without turning one Unity version into a broad
support claim. See [testing.md](testing.md).

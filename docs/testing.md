# Testing strategy

Insider separates deterministic tests from compatibility claims that require a
real Unity player.

## Automated layers

### Managed loader tests

The executable suite in `tests/Insider.Tests` covers plugin discovery,
metadata, duplicate identifiers, failure containment, reverse unload order,
required and optional plugin dependency ordering, missing dependencies, cycles,
failure propagation, numeric version validation, minimum-version enforcement,
plugin-scoped logging, managed detour application/removal, detour cleanup after
unload or failed load, exact-signature rejection, instance-method and
instance-constructor detours with original calls, value-type instance methods
with `ref self`, multi-detour chains, selective removal, cross-plugin ownership
isolation, installation manifests, hash verification, and proxy backup
restoration.

### Managed bootstrap integration fixture

`Insider.PluginFixture` is copied into a temporary Unity-like game layout and
loaded through a real managed bootstrap session. The test verifies runtime
detection, directory creation, assembly discovery, plugin context delivery,
exact dependency resolution, load and unload callbacks, failure-closed behavior
for missing or conflicting dependencies and unsupported runtimes, and
persistent bootstrap logging.

The fixture is test-only and is never included in release packages.

### Native proxy smoke test

`InsiderNativeProxySmoke` loads the generated `version.dll`, verifies all 17
expected Windows version-information exports, and calls
`GetFileVersionInfoSizeW` through the proxy against a system binary.

### Mono bootstrap fixture

`InsiderNativeBootstrapFixture` loads an Insider-owned fake module named
`mono-2.0-bdwgc.dll` before loading the native proxy. The module implements only
the embedding exports consumed by Insider and validates this sequence:

1. resolve the Mono root domain;
2. attach the bootstrap thread;
3. open `Insider/core/Insider.Bootstrap.dll`;
4. resolve `Insider.Native.Entrypoint.Start()`;
5. invoke the static method without arguments or an instance;
6. publish the game executable through `INSIDER_PROCESS_PATH`.

Additional scenarios make the root domain unavailable for the first three
queries to verify polling and retry behavior, and return a managed exception
from `mono_runtime_invoke` to verify that the bootstrap fails closed and writes
the expected diagnostic instead of crashing the host process.

The fixture contains no Unity or Mono code and is never included in release
packages.

### Windows package smoke test

`eng/Test-WindowsPackage.ps1` verifies the assembled artifact before upload. It
checks the native bootstrap, managed core, CLI runtime files and package README;
requires the complete hooking runtime and license notices; rejects test
assemblies and source files; and runs the packaged CLI help command.

### Real Unity Mono smoke test

`eng/Test-UnityMonoSmoke.ps1` builds a minimal player with Unity `2022.3.62f2`,
the Windows x64 target, and the Mono scripting backend. It then builds the
Insider native and managed components, assembles a package, installs it into the
generated player, copies a test plugin, and launches the player in batch mode.

The test succeeds only when all of these observations are present:

1. Unity starts and exits normally after the fixture delay;
2. `version.dll` finds the real Unity Mono runtime;
3. the managed bootstrap reports `UnityMono` and `x64`;
4. MonoMod.RuntimeDetour changes the plugin's managed test method from `7` to
   `42` inside the real Unity Mono runtime;
5. a second detour wraps an instance method, receives `self`, and calls its
   original implementation before producing `42`;
6. a value-type instance detour receives `ref self`, calls the original method,
   produces `42`, and preserves the original mutation in the struct;
7. the plugin observes Unity loading its effective `Assembly-CSharp` instance
   and applies two detours to one static method without a compile-time game
   reference;
8. both continuations contribute to the chain and the player directly observes
   `42` instead of the original `7`;
9. the plugin disposes both game-hook handles while the player remains active;
10. the player directly invokes the same method again and observes the restored
   value `7`;
11. the test plugin writes its load marker and scoped log messages;
12. the other plugin-owned detours remain active through the plugin's
    `Unload()` callback;
13. the managed log contains no error entries;
14. the installed files still pass the CLI status check.

This test is local rather than part of GitHub Actions because it needs an
installed and licensed Unity Editor. Its generated project state, package, and
player stay below `artifacts/unity-mono-smoke` or ignored Unity directories.

## What the fixture does not prove

The fake native runtime cannot execute managed IL or reproduce Unity's Mono
fork. The real-player fixture covers one Unity release and a deliberately empty
game, but it does not validate game-specific behavior, Unity main-thread APIs,
hooks against UnityEngine or production game code, ordered chains or chains
involving multiple real plugins, constructor hooks inside Unity, complex method
signatures, value-type constructors, anti-cheat interaction, or other Unity/Mono
versions. Broader
real-player evidence is still required before compatibility can move from
experimental to supported.

## Run locally

```powershell
dotnet build Insider.slnx --configuration Release
dotnet run --project tests/Insider.Tests --configuration Release --no-build

cmake -S native -B artifacts/native-build -A x64
cmake --build artifacts/native-build --config Release
ctest --test-dir artifacts/native-build --build-config Release --output-on-failure

./eng/Test-WindowsPackage.ps1 -PackageDirectory artifacts/Insider-windows-x64

./eng/Test-UnityMonoSmoke.ps1
```

The Unity smoke script defaults to the Unity Hub installation at
`C:\Program Files\Unity\Hub\Editor\2022.3.62f2`. Pass `-UnityEditor` to use
another executable. It locates CMake from `PATH` or the latest Visual Studio
installation; `-CMake` can override that path when needed.

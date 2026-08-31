# Testing strategy

Insider separates deterministic tests from compatibility claims that require a
real Unity player.

## Automated layers

### Managed loader tests

The executable suite in `tests/Insider.Tests` covers plugin discovery,
metadata, duplicate identifiers, failure containment, reverse unload order,
installation manifests, hash verification, and proxy backup restoration.

### Managed bootstrap integration fixture

`Insider.PluginFixture` is copied into a temporary Unity-like game layout and
loaded through a real managed bootstrap session. The test verifies runtime
detection, directory creation, assembly discovery, plugin context delivery,
load and unload callbacks, failure-closed behavior on unsupported runtimes,
and persistent bootstrap logging.

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

## What the fixture does not prove

The fake native runtime cannot execute managed IL or reproduce Unity's Mono
fork. The managed fixture executes IL on the test host, but does not enter
through the native proxy or validate Unity main-thread behavior. A real Windows
x64 Unity/Mono player is still required before compatibility can move from
experimental to supported.

## Run locally

```powershell
dotnet build Insider.slnx --configuration Release
dotnet run --project tests/Insider.Tests --configuration Release --no-build

cmake -S native -B artifacts/native-build -A x64
cmake --build artifacts/native-build --config Release
ctest --test-dir artifacts/native-build --build-config Release --output-on-failure
```

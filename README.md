# Insider Mod Loader

> A lightweight mod runtime for Unity.

Insider is an independent mod loader and runtime hooking SDK for Unity games. It
provides managed plugin loading, lifecycle APIs, diagnostics, and a foundation
for runtime method hooks.

> [!WARNING]
> Insider is currently pre-alpha. It is not ready for use with production game
> installations.

## Current scope

The first implementation target is intentionally narrow:

- Unity games using the Mono scripting backend
- Windows x64
- Managed plugins loaded from `Insider/plugins`
- A loader-owned plugin lifecycle and diagnostics

IL2CPP and additional operating systems are planned as separate runtime
backends. They are not supported yet.

## Architecture

```text
Unity game
  -> Insider native version.dll proxy
  -> Insider.Bootstrap
  -> Insider.Loader
  -> managed plugins
  -> runtime hooking backend
```

The first native bootstrap is owned by Insider. On Windows x64 it proxies the
system `version.dll`, waits for Unity's Mono runtime, and invokes the managed
entry point through Mono's embedding API. Insider does not depend on BepInEx or
another mod loader. A Doorstop-compatible managed adapter remains available for
integration testing and migration only.

See [docs/architecture.md](docs/architecture.md) for the component boundaries
and [docs/compatibility.md](docs/compatibility.md) for the support policy. The
[testing strategy](docs/testing.md) explains what is automated without a game
fixture and what still requires a real Unity player. Plugin authors should start
with the [plugin development guide](docs/plugin-development.md).

## Repository layout

```text
src/       Maintained loader, SDK, bootstrap, and tooling
native/    Insider-owned Windows process bootstrap
tests/     Dependency-free executable test suite
samples/   Example plugins
legacy/    Archived Insider v1 source; never shipped or built
docs/      Architecture, compatibility, and design decisions
```

## Install a CI build

Download the `Insider-windows-x64` artifact from the latest successful GitHub
Actions run, extract it outside the game directory, and run:

```powershell
dotnet insider.dll inspect "C:\Games\Example\Example.exe"
dotnet insider.dll install "C:\Games\Example\Example.exe"
dotnet insider.dll status "C:\Games\Example\Example.exe"
```

To remove Insider and restore a pre-existing root `version.dll`:

```powershell
dotnet insider.dll uninstall "C:\Games\Example\Example.exe"
```

Installation is deliberately limited to detected Windows x64 Unity/Mono games.
It records hashes in `Insider/install.json`, never removes plugins or logs, and
refuses to uninstall modified loader files unless `--force` is explicitly used.
The pre-alpha CLI package requires the .NET 10 runtime.

## Build

The maintained projects use the .NET 10 SDK. The plugin contracts and managed
loader target .NET Standard 2.0 for compatibility with modern Unity Mono games.

```powershell
dotnet build Insider.slnx --configuration Release
dotnet run --project tests/Insider.Tests --configuration Release --no-build
```

The native bootstrap uses CMake and the MSVC x64 toolchain:

```powershell
cmake -S native -B artifacts/native-build -A x64
cmake --build artifacts/native-build --config Release
ctest --test-dir artifacts/native-build --build-config Release --output-on-failure
```

## Plugin model

Plugins implement `IInsiderPlugin` and declare metadata with
`InsiderPluginAttribute`:

```csharp
using Insider;

[InsiderPlugin("com.example.hello", "Hello Insider", "0.1.0")]
public sealed class HelloPlugin : IInsiderPlugin
{
    public void Load(IInsiderContext context)
    {
        context.Logger.Info("Hello from Insider.");
    }

    public void Unload()
    {
    }
}
```

Plugin-to-plugin requirements use stable plugin IDs and are resolved before any
plugin is activated:

```csharp
[InsiderPluginDependency("com.example.foundation")]
```

The API is deliberately small while the runtime and hook lifecycle are proven
against real Unity players.

Managed dependencies should be placed under `Insider/plugins/dependencies`.
Insider resolves exact assembly identities from that tree and refuses ambiguous
or conflicting versions. Unity Mono has one shared application domain, so two
plugins cannot safely carry different versions of an assembly with the same
simple name. See the plugin development guide for the supported layout and
diagnostics.

## Security

Insider plugins run in the game process with the same permissions as the game.
There is no security sandbox. Only install plugins you trust. See
[SECURITY.md](SECURITY.md) for reporting guidance.

## License

Insider is licensed under the [Apache License 2.0](LICENSE). Third-party
components retain their respective licenses; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Author

Created and maintained by Luca Bottani, known on GitHub as
[@DoctorB](https://github.com/DoctorB).

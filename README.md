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
- Managed method and instance-constructor detours backed by MonoMod.RuntimeDetour,
  including `ref self` for value-type instance methods

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
with the [plugin development guide](docs/plugin-development.md) and use the
[managed hooking guide](docs/hooking.md) for signatures, lifecycle rules, and
complete examples.

## Repository layout

```text
src/       Maintained loader, SDK, bootstrap, hooking backend, and tooling
native/    Insider-owned Windows process bootstrap
tests/     Managed, native, and real-player fixtures
samples/   Example plugins
legacy/    Archived Insider v1 source; never shipped or built
docs/      Architecture, compatibility, usage guides, and design decisions
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

The Windows artifact is checked after assembly for required runtime files,
license notices, accidental test/source content, and a working packaged CLI.
Run the same check locally with:

```powershell
./eng/Test-WindowsPackage.ps1 -PackageDirectory artifacts/Insider-windows-x64
```

A local smoke fixture builds and launches a real Unity 2022.3 Windows x64
player using the Mono scripting backend. It installs Insider, loads a test
plugin, applies managed detours including one against the player's
`Assembly-CSharp`, removes a two-node hook chain while the player is still
running, verifies the original result is restored, checks native and managed
diagnostics, and checks plugin unload on process exit:

```powershell
./eng/Test-UnityMonoSmoke.ps1
```

The script uses Unity `2022.3.62f2` from its default Unity Hub location unless
`-UnityEditor` is supplied. Generated player files remain under
`artifacts/unity-mono-smoke` and are not committed. This single fixture keeps
the backend experimental; it is evidence for one controlled player, not a
general Unity compatibility claim.

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
[InsiderPluginDependency("com.example.foundation", "1.2.0")]
```

Versions deliberately use only `MAJOR.MINOR.PATCH`, and dependencies support one
simple constraint: an optional minimum version.

The initial hooking API is deliberately small. Plugins can apply a managed
method or instance-constructor detour through
`context.Hooks.Detour(target, replacement)`. The returned
handle removes it early when disposed; Insider also removes every remaining
plugin-owned detour automatically after `Unload()` or a failed `Load()`. A
replacement may accept an original-call delegate first, allowing it to wrap
rather than completely replace game behavior. Multiple detours may share a
target; each returned handle removes only its own detour, while inter-plugin
execution order remains intentionally unspecified. Reference-type instance
methods receive `self`; value-type instance methods receive `ref self` so their
mutations affect the original struct. The [managed hooking guide](docs/hooking.md)
documents every supported signature with examples.

Messages written through `context.Logger` are automatically prefixed with the
plugin ID, keeping the shared game log readable without extra logging APIs.

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

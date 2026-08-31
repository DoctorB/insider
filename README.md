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
  -> native bootstrap
  -> Insider.Bootstrap
  -> Insider.Loader
  -> managed plugins
  -> runtime hooking backend
```

The native bootstrap is a replaceable boundary. The first integration target is
[UnityDoorstop](https://github.com/NeighTools/UnityDoorstop), but Insider does
not depend on BepInEx or another full mod loader.

See [docs/architecture.md](docs/architecture.md) for the component boundaries
and [docs/compatibility.md](docs/compatibility.md) for the support policy.

## Repository layout

```text
src/       Maintained loader, SDK, bootstrap, and tooling
tests/     Dependency-free executable test suite
samples/   Example plugins
legacy/    Archived Insider v1 source; never shipped or built
docs/      Architecture, compatibility, and design decisions
```

## Build

The maintained projects use the .NET 10 SDK. The plugin contracts and managed
loader target .NET Standard 2.0 for compatibility with modern Unity Mono games.

```powershell
dotnet build Insider.slnx --configuration Release
dotnet run --project tests/Insider.Tests --configuration Release --no-build
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

The API is deliberately small while the runtime and hook lifecycle are proven
against real Unity players.

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

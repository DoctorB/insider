# ADR 0016: Essential IL2CPP backend uses a private CoreCLR host

## Status

Accepted

## Context

Unity IL2CPP games do not contain a Mono runtime or managed game methods. The
existing Mono embedding path, reflected `MethodBase` hooks, IL rewriting, and
Unity synchronization-context pump therefore cannot be reused as if IL2CPP were
another Mono version.

Insider still needs to load its managed plugin contract without requiring
BepInEx, another loader, or a machine-wide .NET installation. A complete
generated proxy model would add a large version-sensitive toolchain before the
first release.

## Decision

The Windows x64 package contains one private self-contained .NET runtime under
`Insider/runtime/win-x64`. When `GameAssembly.dll` is present, the native
`version.dll` bootstrap uses `hostfxr_initialize_for_dotnet_command_line` and
`hostfxr_run_app` to start `Insider.Il2CppHost` inside the game process. The same
loader, lifecycle, logging, plugin directories, dependency rules, and cleanup
model then run on CoreCLR.

The first public IL2CPP surface is deliberately small:

- `context.Il2Cpp` resolves `GameAssembly.dll` exports, metadata method pointers,
  and their native code addresses;
- `context.Hooks.DetourNative` installs loader-owned native detours;
- runtime capability flags say explicitly which APIs are available.

Managed `MethodBase` detours, managed IL hooks, automatic Unity type proxies,
and Unity main-thread callbacks are not advertised by this backend. Unsupported
main-thread calls fail immediately with a readable exception.

## Consequences

- Insider remains an independent loader and the game machine needs no separate
  .NET installation.
- The Windows package is larger because it carries one bounded private runtime.
- Native signatures and IL2CPP metadata layouts are game- and Unity-version
  sensitive; an incorrect delegate can crash the process.
- Plugins must branch on `context.Runtime` capabilities and must not treat Mono
  and IL2CPP hooks as interchangeable.
- A packaged smoke fixture must execute the real private CoreCLR path in CI.
- Automatic proxy generation and a proven IL2CPP main-thread pump remain future
  work and require their own evidence before being added to the contract.

## References

- [.NET native hosting design](https://github.com/dotnet/runtime/blob/main/docs/design/features/native-hosting.md)
- [Official NativeHost sample](https://github.com/dotnet/samples/tree/main/core/hosting/src/NativeHost)

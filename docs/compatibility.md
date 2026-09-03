# Compatibility

Compatibility is tracked by runtime backend, operating system, architecture, and
test evidence. A Unity version alone is not a sufficient compatibility claim.

| Backend | Operating system | Architecture | Status |
| --- | --- | --- | --- |
| Unity Mono | Windows | x64 | Experimental; real-player smoke passed on Unity 2022.3.62f2 |
| Unity Mono | Windows | x86 | Planned |
| Unity Mono | Linux/macOS | Any | Planned |
| Unity IL2CPP | Any | Any | Not implemented |

## Definitions

- **First target:** active implementation scope, not a supported release.
- **Experimental:** demonstrated in a fixture but not covered by a stable policy.
- **Supported:** covered by automated fixtures and a documented release policy.
- **Planned:** no compatibility promise.

## Runtime detection

`Insider.Cli inspect` uses the executable architecture and standard Unity player
layout to report likely Mono or IL2CPP use. Detection is diagnostic and does not
replace an end-to-end launch test.

`Insider.Cli diagnose` includes the same detection in a larger read-only report
covering installation and plugin health. It does not launch the player, so a
clean report proves structural consistency rather than runtime compatibility.

## Native bootstrap assumptions

The experimental Windows x64 bootstrap relies on the game loading a local
`version.dll` and on Unity exporting the standard Mono embedding functions from
`mono-2.0-bdwgc.dll`, `mono-2.0-sgen.dll`, or `mono.dll`. Games that do not meet
both conditions require a different bootstrap adapter and are not currently
supported.

The automated fake-Mono fixture validates only the embedding calls made by the
native bootstrap. It does not execute managed assemblies or model Unity's main
thread and therefore does not change the support status by itself.

## Real-player evidence

The local `UnityMonoSmoke` fixture builds a development player with Unity
`2022.3.62f2`, the Mono scripting backend, and the Windows x64 target. On
2026-08-31 it verified the complete path from the local `version.dll` proxy to
the managed bootstrap, plugin discovery, plugin load, process-exit unload, and
persistent native and managed logs. On 2026-09-01 the same fixture also applied
a MonoMod.RuntimeDetour managed hook inside Unity Mono and observed the expected
replacement result during plugin load and unload. The fixture now also wraps a
method with `ref` and `out` parameters and observes both mutations through its
original-call delegate. It also preserves an `in` parameter and a managed
by-reference return; the latter reaches replacement storage, remains writable,
and propagates mutation through the original-call delegate. A reference-type
instance method receives its `self` argument and invokes the original method
synchronously. Virtual base and override implementations are hooked
independently and remain distinct through virtual dispatch. A plugin-owned
value-type method is also wrapped with `ref self`; the original mutation remains
visible in the struct. Finally, the fixture waits for the `Assembly-CSharp`
instance loaded by Unity, detours a method without referencing the game assembly
at compile time, composes two continuations on that target, and observes the
changed result from a direct player call. It then disposes both detour handles
while the player remains active and observes a later direct call return the
original value, proving live chain removal for this controlled player. The same
fixture uses `ModifyIl` on a second `Assembly-CSharp` method, changes its direct
result from `7` to `42`, removes the IL hook while the player remains active,
and observes `7` again. Loading that plugin also verifies that MonoMod/Cecil
contract assemblies are resolved from the host-owned `Insider/core` directory.

On 2026-09-02 the fixture also verified the loader-owned Unity main-thread
dispatcher. Plugin `Load()` ran on managed thread 3, while its posted callback
ran on Unity's managed thread 1 with `IsReady` and `IsCurrent` true. The callback
observed `UnityEngine.UnitySynchronizationContext` as the current synchronization
context and read `UnityEngine.Application.isPlaying` as true. This establishes
the dispatcher contract for the controlled player, not arbitrary Unity API or
Unity-version compatibility.

The fixture is repeatable through `eng/Test-UnityMonoSmoke.ps1`, but it is not
run in GitHub Actions because hosted execution would require a Unity Editor and
license. One controlled player does not establish compatibility with other
Unity releases, game-specific native imports, anti-cheat systems, or modified
Mono runtimes, so the backend remains experimental.

The managed suite separately verifies IL chains, detour coexistence,
value-type constructor rewriting, validation, loader ownership, and retryable
cleanup. This evidence keeps IL hooking within the already experimental Unity
Mono status; it does not establish broad game or Unity-version compatibility.

## Managed dependency constraints

Unity Mono plugins share the game's application domain. Insider resolves exact
managed assembly identities from `Insider/core`, `Insider/plugins`, and the
plugin `dependencies` subtree, but it cannot guarantee side-by-side isolation for two versions with
the same simple assembly name. Duplicate candidates, conflicting versions, and
conflicts with an already loaded game assembly fail closed and are written to
`Insider/logs/insider.log`.

The current managed and native logs are replaced on every process start after
their prior contents are moved to `insider.previous.log` and
`native.previous.log`. Insider retains no older sessions and exposes no logging
configuration. Rotation failures are non-fatal.

When the game has already loaded the exact requested identity, the runtime may
reuse that resident assembly. Insider cannot replace or independently unload it.

Managed assemblies remain loaded until the game process exits even after their
plugin lifecycle receives `Unload()`.

## Legacy Insider v1

The archived v1 implementation targets .NET Framework 3.5 and writes directly to
JIT code memory. It is retained for provenance only and is not part of the modern
compatibility matrix.

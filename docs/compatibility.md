# Compatibility

Compatibility is tracked by runtime backend, operating system, architecture, and
test evidence. A Unity version alone is not a sufficient compatibility claim.

| Backend | Operating system | Architecture | Status |
| --- | --- | --- | --- |
| Unity Mono | Windows | x64 | Experimental; ABI fixture automated, real game validation pending |
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

## Native bootstrap assumptions

The experimental Windows x64 bootstrap relies on the game loading a local
`version.dll` and on Unity exporting the standard Mono embedding functions from
`mono-2.0-bdwgc.dll`, `mono-2.0-sgen.dll`, or `mono.dll`. Games that do not meet
both conditions require a different bootstrap adapter and are not currently
supported.

The automated fake-Mono fixture validates only the embedding calls made by the
native bootstrap. It does not execute managed assemblies or model Unity's main
thread and therefore does not change the support status by itself.

## Legacy Insider v1

The archived v1 implementation targets .NET Framework 3.5 and writes directly to
JIT code memory. It is retained for provenance only and is not part of the modern
compatibility matrix.

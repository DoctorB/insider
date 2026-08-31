# ADR 0003: Own the first Windows bootstrap

- Status: Accepted
- Date: 2026-08-31

## Context

Insider needs to provide value independently of an existing mod loader. The
managed boundary from ADR 0002 remains useful, but distributing another
project's bootstrap as the primary entry mechanism would leave a core part of
the product outside Insider's control.

## Decision

Insider owns its first Windows x64 bootstrap. A local `version.dll` proxy
forwards the operating-system version API and uses the Mono embedding API that
is already present in Unity/Mono games to invoke `Insider.Native.Entrypoint`.

The bootstrap remains an adapter behind the managed entry boundary. The
Doorstop-compatible entry point is retained for tests and migration, but it is
not the default packaged path.

## Consequences

- Insider can be installed and run without BepInEx or Doorstop.
- Native ABI correctness and proxy compatibility become Insider's
  responsibility.
- The first package is intentionally Windows x64 and Unity/Mono only.
- End-to-end game fixtures are required before changing the status from
  experimental to supported.

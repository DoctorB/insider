# ADR 0001: Build a loader-owned Unity mod runtime

- Status: Accepted; extended by ADR 0016 for IL2CPP
- Date: 2026-08-31

## Context

Insider v1 was a small managed hooking library. A modern wrapper around an
existing detour library would add little value without owning plugin discovery,
lifecycle, diagnostics, and installation.

## Decision

Insider will be an independent Unity mod loader and SDK. It will be loader
agnostic at its public API boundary and will not depend on BepInEx or MelonLoader.
The first implementation target was Unity Mono on Windows x64. ADR 0016 adds a
separate essential Windows x64 IL2CPP backend.

## Consequences

- The project owns a plugin contract and managed chainloader.
- Native bootstrapping remains a replaceable adapter.
- IL2CPP is a separate backend and will not be simulated through Mono behavior.
- Scope is larger than a hooking package, so compatibility claims require player
  fixtures and installation tests.

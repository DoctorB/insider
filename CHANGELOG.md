# Changelog

All notable changes to Insider will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial repository structure and project governance.
- Managed plugin contracts and chainloader foundation.
- Unity runtime inspection CLI.
- Doorstop-compatible managed bootstrap entry point.
- Archived Insider v1 source for historical reference.
- Insider-owned Windows x64 `version.dll` bootstrap for Unity Mono.
- Safe CLI install, status, and uninstall commands with SHA-256 manifests.
- Windows x64 CI package artifact containing a framework-dependent .NET 10 CLI.
- Native fake-Mono fixture covering the bootstrap call sequence, delayed domain
  availability, and managed invocation failures.
- Managed bootstrap integration fixture covering runtime detection, plugin
  discovery, lifecycle callbacks, logging, and unsupported runtimes.
- Deterministic plugin dependency catalog with exact identity resolution and
  failure-closed diagnostics for missing or conflicting assemblies.
- Plugin development guide and dependency-resolution architecture decision.
- Required and optional plugin-ID dependencies with deterministic graph-based
  activation, cycle detection, and failure propagation.
- Simple `MAJOR.MINOR.PATCH` plugin versions and inclusive minimum-version
  requirements, without a range-expression language.
- Automatic plugin-ID prefixes for messages written through the plugin context
  logger.
- CI verification of required Windows package files and license notices,
  test/source exclusions, and the packaged CLI entry point.
- Local Unity 2022.3 Windows x64 Mono smoke player covering the complete native
  bootstrap, managed loader, plugin load/unload, diagnostics, and installer
  status path.
- Minimal `IInsiderHookService` API and a MonoMod.RuntimeDetour 25.3.6 backend
  for direct managed method detours.
- Plugin-owned detour cleanup after normal unload and failed plugin activation,
  plus managed and real Unity Mono hook fixtures.
- Plugin-scoped assembly resolution that leaves core hooking dependencies to the
  host without false missing-dependency errors.
- Exact hook-signature validation plus reference-type instance detours and
  synchronous original-method continuations, verified in managed and Unity Mono
  fixtures.
- Real Unity Mono coverage for a plugin that waits for Unity's effective
  `Assembly-CSharp` instance and detours a method invoked directly by the player.
- Multi-detour chain coverage with selective removal and failed-plugin cleanup
  that preserves hooks owned by other plugins, plus a two-node Unity game hook.
- Instance-constructor detours through the same minimal `MethodBase` API,
  including exact `void` signatures, `self`, synchronous original calls, and
  deterministic removal coverage.
- Real Unity Mono coverage that disposes a two-node game hook chain while the
  player remains active and verifies the direct call changes from `42` back to
  the original `7`.
- Value-type instance method detours with exact `ref self` signatures and
  original-call continuations, verified in managed tests and a real Unity Mono
  player.
- A canonical managed hooking guide with complete signature mappings, lifecycle
  rules, Unity assembly-loading guidance, and examples for every supported
  detour form.
- Explicit `ref` and `out` parameter support with readable by-reference
  diagnostics, managed mutation/restoration tests, and real Unity Mono
  coverage.
- Independently targeted virtual base and override detours, including exact
  `self` signatures, selective removal tests, real Unity Mono coverage, and
  documented `DeclaredOnly` reflection guidance.
- `in` parameters and managed by-reference returns within the existing
  exact-signature hook contract, plus explicit fail-closed rejection for generic
  methods and members of generic types unsupported by RuntimeDetour, verified
  through managed tests and a real Unity Mono player.
- Idempotent, retryable detour removal that keeps failed handles under plugin
  ownership, attempts the remaining cleanup, and reports aggregate failures.
- Stable `InsiderHookException` wrapping backend application and removal
  failures, plus target-aware readable signature diagnostics and explicit
  multicast-delegate rejection.
- Loader-owned `ModifyIl` hooks backed by MonoMod `ILContext`, including managed
  IL-body validation, multicast rejection, composable independently removable
  manipulators, retryable cleanup, stable diagnostics, and complete usage
  documentation.
- Managed IL-hook coverage for rewriting and restoration, chains, detour
  coexistence, value-type constructors, validation, ownership, and retryable
  cleanup, plus a real Unity Mono `Assembly-CSharp` rewrite and live removal.
- Deterministic resolution of public Insider and MonoMod/Cecil dependencies from
  the host-owned `Insider/core` directory, without plugin-private copies.
- Loader-owned Unity Mono main-thread dispatch through `context.MainThread`,
  with FIFO scheduling, per-plugin cancellation and failure containment,
  managed coverage, real Unity player verification, and usage documentation.

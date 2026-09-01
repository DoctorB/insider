# ADR 0007: Use RuntimeDetour behind a minimal hook service

- Status: Accepted
- Date: 2026-09-01

## Context

Insider needs managed runtime hooks without reviving the archived v1 strategy of
writing JIT instructions directly. The first backend must work in Unity Mono,
remain replaceable, and avoid exposing a third-party API as the permanent plugin
contract.

## Decision

Insider uses MonoMod.RuntimeDetour 25.3.6 behind `IInsiderHookService`. Its
single public operation accepts a managed `MethodBase` and compatible
replacement `Delegate`, applies the detour immediately, and returns an
`IDisposable` removal handle. The supported targets are methods and instance
constructors. Signatures must match exactly. Reference-type instance members
add `self` before their declared parameters; value-type instance methods add
`ref self` so mutations reach the original struct. Declared managed by-reference
parameters, including C# `ref` and `out`, retain their position and propagate
mutations through replacements and original calls; `in` parameters and managed
by-reference returns also retain their CLR by-reference types. Open generic
targets, closed generic methods, and members declared on generic types are
rejected before patching because RuntimeDetour does not support generic source
hooks and Mono may share their generated code. Virtual base methods and
overrides are targeted as distinct implementations. Constructors have a `void`
return type.
A replacement may prepend an original-call delegate with the target signature
and invoke it synchronously. Multiple detours may compose through that
continuation, while each removal handle affects only its own node. Insider does
not define their inter-plugin execution order.

The loader scopes every handle to the plugin that created it. Handles are
removed in reverse creation order after `Unload()` and are also removed when
`Load()` fails. Plugins may dispose a handle earlier. Successful disposal is
idempotent. A failed removal leaves the handle tracked so it can be retried,
rather than reporting success for a possibly active detour. Runtime application
and removal failures are exposed as `InsiderHookException` with the underlying
backend exception preserved; contract validation continues to use standard
argument and unsupported-operation exceptions.

Static constructors, value-type constructors, variable-argument methods, IL
hooks, HookGen, native detours, ordering controls, and third-party types are
outside this first public contract. IL hooks are added later by
[ADR 0008](0008-il-hook-contract.md) without changing the detour signature.

## Consequences

- Plugins receive one small hooking abstraction instead of depending on
  MonoMod directly.
- Plugins can wrap game behavior without exposing a MonoMod-specific type.
- Plugin failure cleanup preserves detours owned by other plugins in the same
  chain.
- Cleanup attempts all context-owned detours and reports failures together;
  failed removal handles are not silently discarded.
- The Windows package now redistributes the RuntimeDetour dependency closure;
  versions, sources, licenses, and binary hashes are recorded in
  `THIRD_PARTY_NOTICES.md`.
- Signature compatibility and runtime support remain experimental and require
  broader real-game evidence.

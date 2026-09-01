# ADR 0008: Expose ILContext for advanced IL hooks

- Status: Accepted
- Date: 2026-09-01

## Context

Whole-method detours cover common mods, but some changes need to preserve most
of a method and edit one instruction sequence. Insider must support that use
case without building and maintaining a second incomplete model of CIL
instructions, operands, labels, locals, and exception regions.

Keeping every MonoMod type out of the public contract would require either a
large Insider-specific IL abstraction or an untyped callback. The first would
violate the project's simplicity goal and the second would discard compile-time
safety while still requiring plugins to understand the backend model.

## Decision

`IInsiderHookService` gains one operation:

```csharp
IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator);
```

`ILContext` is the advanced boundary and comes from the pinned MonoMod.Utils
25.0.14 dependency. MonoMod.RuntimeDetour's `ILHook` applies the manipulator.
Insider does not wrap `ILCursor`, Cecil instructions, opcodes, labels, locals,
or exception handlers; plugins can use their complete established API.

The service validates that the target is a non-generic managed method or
instance constructor with a readable IL body. Abstract, external, P/Invoke,
runtime-provided, generic, variable-argument, and static-constructor targets are
rejected before patching. Value-type instance constructors are valid IL targets
even though signature-based `Detour` does not support them. Multicast
manipulators are rejected.

Every returned handle uses the existing plugin ownership contract: immediate
application, independent removal, reverse-order cleanup after unload or failed
load, idempotent successful disposal, retry after removal failure, and
`InsiderHookException` around backend failures. Multiple IL hooks and managed
detours may share a target, but Insider exposes no priority or ordering API.

MonoMod may run manipulators again while rebuilding a target's IL-hook chain.
Manipulators must be deterministic, must not retain callback-scoped objects,
and must fail closed when an expected instruction pattern is absent.

## Consequences

- Plugins get the complete IL manipulation surface through one additional
  method instead of an Insider-specific instruction framework.
- The detour API remains backend-neutral; plugins using `ModifyIl` deliberately
  accept a compile-time dependency on the pinned MonoMod/Cecil model.
- Insider ships and owns those runtime assemblies. Plugins must not redistribute
  private copies into the shared Unity application domain.
- Insider can validate target shape and lifecycle, but only the manipulator can
  guarantee valid stack behavior, branches, and exception regions.
- Runtime and Unity evidence for IL hooks is gathered in the separate test phase
  before compatibility claims are expanded.

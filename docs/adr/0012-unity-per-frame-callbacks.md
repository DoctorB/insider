# ADR 0012: Register scoped Unity per-frame callbacks

- Status: Accepted
- Date: 2026-09-03

## Context

Some plugins need a small operation on every Unity frame. Requiring each plugin
to create a `MonoBehaviour` adds Unity object lifecycle and version-specific
setup to a use case already covered by Insider's main-thread pump.

A general player-loop abstraction, event bus, scheduler, or timing framework
would be larger than the requirement.

## Decision

`IInsiderMainThread` adds one method:

```csharp
IDisposable RegisterUpdate(Action callback);
```

For Unity Mono, active registrations run once after each intercepted
`UnitySynchronizationContext.ExecuteTasks()` call, following the current FIFO
`Post` snapshot. Registrations created during a pump begin on the next pump.
The returned idempotent handle removes one registration.

The plugin-scoped main-thread wrapper owns every returned inner handle. It
disposes remaining registrations after `Unload()` or a failed `Load()`, while
still allowing the plugin to dispose a handle earlier. Callback failures are
logged with the plugin ID and do not stop other callbacks or implicitly remove
the failing registration.

## Consequences

- Plugins can perform short per-frame Unity work without creating a component.
- Inactive plugins cannot leave update callbacks in the player loop.
- Ordering is simple and deterministic within Insider: posted work first, then
  updates in registration order.
- The API provides no interval, priority, result, fixed-update, late-update, or
  general player-loop phase model.
- Compatibility continues to depend on the Unity Mono synchronization pump.

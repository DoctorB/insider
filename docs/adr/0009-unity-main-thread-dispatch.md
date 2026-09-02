# ADR 0009: Dispatch through Unity's synchronization pump

- Status: Accepted
- Date: 2026-09-02

## Context

Insider enters Mono from its native bootstrap thread and loads plugins before
Unity APIs can be assumed ready. Calling Unity from `Load()` is therefore unsafe,
but moving the whole loader lifecycle onto Unity's main thread would make
discovery asynchronous and complicate startup, failure reporting, and plugin
dependency ordering.

Plugins need one narrow way to schedule Unity work without making the public
contract depend on `UnityEngine` or building a general job framework.

## Decision

`IInsiderContext` exposes an `IInsiderMainThread` service with three members:

```csharp
bool IsReady { get; }
bool IsCurrent { get; }
void Post(Action callback);
```

For Unity Mono, the bootstrap observes the effective
`UnityEngine.CoreModule` and installs a loader-owned detour on the internal
`UnitySynchronizationContext.ExecuteTasks()` method. The replacement invokes
Unity's original pump first, records the current managed thread, and drains one
FIFO snapshot of Insider callbacks. Work posted during that drain waits until
the next pump.

The pump is installed only after the managed dependency resolver is active.
Plugin contexts wrap the shared queue so pending work becomes inert after
failed load or unload. Callback exceptions are logged with the plugin ID and do
not stop later work.

## Consequences

- Plugins can safely enter the Unity main thread without a public Unity
  dependency.
- Plugin discovery and activation remain deterministic on the bootstrap thread.
- The first API has no synchronous invoke, tasks, cancellation, priorities,
  delays, or repeating callbacks.
- `Unload()` remains off the main thread and queued work is invalidated when the
  plugin context is disposed.
- Compatibility depends on the internal Unity synchronization pump and must be
  verified against real players; failure leaves the dispatcher unready and is
  logged without preventing plugin discovery.

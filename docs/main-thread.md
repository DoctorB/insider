# Unity main-thread guide

Insider loads plugins from its early bootstrap thread. Unity APIs are generally
not safe to call from that thread. Use `context.MainThread` to queue the smallest
piece of work that must run on Unity's main thread:

```csharp
using System;
using Insider;
using UnityEngine;

public void Load(IInsiderContext context)
{
    context.MainThread.Post(() =>
    {
        if (!context.MainThread.IsCurrent)
        {
            throw new InvalidOperationException("Expected the Unity main thread.");
        }

        Debug.Log("Hello from Unity's main thread.");
    });
}
```

`Post` is thread-safe, returns immediately, and accepts work even before the
Unity pump is ready. `IsReady` becomes `true` after Insider observes the first
main-thread pump. `IsCurrent` is `true` only on the thread used by that pump.

## Queue behavior

- Callbacks run in FIFO order after Unity has processed its own synchronization
  context work for that frame.
- A callback posted while the queue is being drained waits for the next frame.
- One callback failure is logged with the plugin ID and does not stop the
  remaining callbacks.
- Keep callbacks short. Blocking the callback blocks the Unity player loop.

Do not spin or poll `IsReady`; post the work and let Insider deliver it. The
properties are useful for diagnostics and assertions, not for scheduling.

## Per-frame updates

Use `RegisterUpdate` for a small callback that must run once per Unity pump:

```csharp
using System;
using Insider;
using UnityEngine;

private IDisposable? _update;

public void Load(IInsiderContext context)
{
    _update = context.MainThread.RegisterUpdate(() =>
    {
        if (!context.MainThread.IsCurrent)
        {
            throw new InvalidOperationException("Expected the Unity main thread.");
        }

        Debug.Log($"Frame {Time.frameCount}");
    });
}

public void Unload()
{
    _update?.Dispose();
    _update = null;
}
```

The returned handle removes only that registration. Disposal is thread-safe and
idempotent. Explicit disposal is useful when the work finishes early; Insider
also disposes every remaining registration automatically after `Unload()` or a
failed `Load()`.

Updates run in registration order after the current `Post` queue snapshot. A
registration created while the pump is running starts on the next pump. A
callback can dispose itself or another registration; a disposed registration
does not run again. One update failure is logged with the plugin ID, does not
stop other updates, and does not remove the failing registration.

`RegisterUpdate` is thread-safe, returns immediately, and accepts callbacks
before the Unity pump is ready. Keep per-frame work especially short: it runs
inside Unity's player loop, so blocking it blocks the frame.

## Plugin ownership

Every plugin receives a scoped main-thread service. Pending callbacks are
invalidated and update registrations are disposed after `Unload()` and after a
failed `Load()`, so code from an inactive plugin cannot run later. Posting or
registering through a retained scoped service after that point throws
`ObjectDisposedException`.

`Unload()` itself is not a main-thread callback. Work posted from `Unload()` is
invalidated when the callback returns and should not be used for Unity cleanup.
Release main-thread resources before unload or from an earlier scheduled
callback.

## Unity references

The Insider contract does not reference `UnityEngine`. A plugin may compile
against the target game's Unity assemblies when it needs Unity types, but it
must not redistribute those engine assemblies. Reflection remains an option for
plugins that deliberately avoid a compile-time Unity dependency.

The current Unity Mono implementation observes the effective
`UnityEngine.CoreModule` assembly and detours the internal
`UnitySynchronizationContext.ExecuteTasks()` pump. It calls Unity's original
method first, drains one snapshot of the Insider queue, and then invokes one
snapshot of active update registrations. If the expected pump is unavailable or
cannot be hooked, Insider logs the failure and `IsReady` remains `false`.

## Current limits

The initial API deliberately provides no synchronous invoke, result value,
`Task`, cancellation token, priority, delayed scheduling, fixed-time callback,
or general player-loop event model. These features can be added later only when
a concrete use case justifies the extra contract.

This dispatcher supports the experimental Windows x64 Unity Mono backend only.
The essential IL2CPP context still exposes the interface so plugins can share a
contract, but reports `SupportsMainThread == false`; `Post` and `RegisterUpdate`
throw a readable `NotSupportedException`. It does not guess at a player loop.

using System;

namespace Insider;

public interface IInsiderMainThread
{
    bool IsReady { get; }

    bool IsCurrent { get; }

    void Post(Action callback);

    /// <summary>
    /// Registers a callback that runs once per Unity main-thread pump.
    /// </summary>
    /// <returns>An idempotent handle that removes the registration.</returns>
    IDisposable RegisterUpdate(Action callback);
}

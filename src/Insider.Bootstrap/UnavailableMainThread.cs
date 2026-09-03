using System;

namespace Insider.Bootstrap;

internal sealed class UnavailableMainThread : IInsiderMainThread
{
    private const string Message =
        "Unity main-thread dispatch is not available on the essential IL2CPP backend yet.";

    public bool IsReady => false;

    public bool IsCurrent => false;

    public void Post(Action callback)
    {
        _ = callback ?? throw new ArgumentNullException(nameof(callback));
        throw new NotSupportedException(Message);
    }

    public IDisposable RegisterUpdate(Action callback)
    {
        _ = callback ?? throw new ArgumentNullException(nameof(callback));
        throw new NotSupportedException(Message);
    }
}

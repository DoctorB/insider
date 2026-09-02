using System;

namespace Insider.Loader;

internal sealed class PluginMainThread : IInsiderMainThread, IDisposable
{
    private readonly IInsiderMainThread _inner;
    private readonly IInsiderLogger _logger;
    private readonly object _sync = new object();
    private bool _disposed;

    public PluginMainThread(IInsiderMainThread inner, IInsiderLogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsReady
    {
        get
        {
            lock (_sync)
            {
                return !_disposed && _inner.IsReady;
            }
        }
    }

    public bool IsCurrent
    {
        get
        {
            lock (_sync)
            {
                return !_disposed && _inner.IsCurrent;
            }
        }
    }

    public void Post(Action callback)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            _inner.Post(() => Run(callback));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    private void Run(Action callback)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                _logger.Error("Main-thread callback failed.", exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginMainThread));
        }
    }
}

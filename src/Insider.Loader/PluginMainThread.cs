using System;
using System.Collections.Generic;
using System.Threading;

namespace Insider.Loader;

internal sealed class PluginMainThread : IInsiderMainThread, IDisposable
{
    private readonly IInsiderMainThread _inner;
    private readonly IInsiderLogger _logger;
    private readonly object _sync = new object();
    private readonly List<UpdateRegistration> _updates = new List<UpdateRegistration>();
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

    public IDisposable RegisterUpdate(Action callback)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            var registration = new UpdateRegistration(
                this,
                _inner.RegisterUpdate(() => Run(callback, "Main-thread update callback failed.")));
            _updates.Add(registration);
            return registration;
        }
    }

    public void Dispose()
    {
        UpdateRegistration[] updates;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            updates = _updates.ToArray();
            _updates.Clear();
        }

        foreach (var update in updates)
        {
            update.DisposeFromOwner();
        }
    }

    private void Run(Action callback)
    {
        Run(callback, "Main-thread callback failed.");
    }

    private void Run(Action callback, string failureMessage)
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
                _logger.Error(failureMessage, exception);
            }
        }
    }

    private void RemoveUpdate(UpdateRegistration registration)
    {
        lock (_sync)
        {
            _updates.Remove(registration);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginMainThread));
        }
    }

    private sealed class UpdateRegistration : IDisposable
    {
        private readonly PluginMainThread _owner;
        private IDisposable? _inner;

        public UpdateRegistration(PluginMainThread owner, IDisposable inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public void Dispose()
        {
            var inner = Interlocked.Exchange(ref _inner, null);
            if (inner is null)
            {
                return;
            }

            _owner.RemoveUpdate(this);
            inner.Dispose();
        }

        public void DisposeFromOwner()
        {
            Interlocked.Exchange(ref _inner, null)?.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Insider.Loader;

internal sealed class PluginHookService : IInsiderHookService, IDisposable
{
    private readonly object _sync = new object();
    private readonly IInsiderHookService _inner;
    private readonly List<OwnedDetour> _detours = new List<OwnedDetour>();
    private bool _disposed;

    public PluginHookService(IInsiderHookService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PluginHookService));
            }

            var detour = new OwnedDetour(_inner.Detour(target, replacement), Release);
            _detours.Add(detour);
            return detour;
        }
    }

    public void Dispose()
    {
        OwnedDetour[] detours;
        lock (_sync)
        {
            if (_disposed && _detours.Count == 0)
            {
                return;
            }

            _disposed = true;
            detours = _detours.ToArray();
        }

        List<Exception>? failures = null;
        for (var index = detours.Length - 1; index >= 0; index--)
        {
            try
            {
                detours[index].Dispose();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more plugin detours could not be removed.", failures);
        }
    }

    private void Release(OwnedDetour detour)
    {
        lock (_sync)
        {
            _detours.Remove(detour);
        }
    }

    private sealed class OwnedDetour : IDisposable
    {
        private readonly object _sync = new object();
        private readonly IDisposable _inner;
        private readonly Action<OwnedDetour> _release;
        private bool _disposed;

        public OwnedDetour(IDisposable inner, Action<OwnedDetour> release)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _inner.Dispose();
                _disposed = true;
            }

            _release(this);
        }
    }
}

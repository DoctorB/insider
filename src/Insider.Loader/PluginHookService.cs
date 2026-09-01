using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.Cil;

namespace Insider.Loader;

internal sealed class PluginHookService : IInsiderHookService, IDisposable
{
    private readonly object _sync = new object();
    private readonly IInsiderHookService _inner;
    private readonly List<OwnedHook> _hooks = new List<OwnedHook>();
    private bool _disposed;

    public PluginHookService(IInsiderHookService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        return Own(() => _inner.Detour(target, replacement));
    }

    public IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator)
    {
        return Own(() => _inner.ModifyIl(target, manipulator));
    }

    public void Dispose()
    {
        OwnedHook[] hooks;
        lock (_sync)
        {
            if (_disposed && _hooks.Count == 0)
            {
                return;
            }

            _disposed = true;
            hooks = _hooks.ToArray();
        }

        List<Exception>? failures = null;
        for (var index = hooks.Length - 1; index >= 0; index--)
        {
            try
            {
                hooks[index].Dispose();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more plugin hooks could not be removed.", failures);
        }
    }

    private IDisposable Own(Func<IDisposable> create)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PluginHookService));
            }

            var hook = new OwnedHook(create(), Release);
            _hooks.Add(hook);
            return hook;
        }
    }

    private void Release(OwnedHook hook)
    {
        lock (_sync)
        {
            _hooks.Remove(hook);
        }
    }

    private sealed class OwnedHook : IDisposable
    {
        private readonly object _sync = new object();
        private readonly IDisposable _inner;
        private readonly Action<OwnedHook> _release;
        private bool _disposed;

        public OwnedHook(IDisposable inner, Action<OwnedHook> release)
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

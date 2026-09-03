using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Insider.Bootstrap;

internal sealed class UnityMonoMainThread : IInsiderMainThread, IDisposable
{
    private const string UnityCoreAssemblyName = "UnityEngine.CoreModule";
    private const string UnitySynchronizationContextTypeName = "UnityEngine.UnitySynchronizationContext";
    private const string ExecuteTasksMethodName = "ExecuteTasks";

    private readonly Queue<Action> _callbacks = new Queue<Action>();
    private readonly List<UpdateRegistration> _updates = new List<UpdateRegistration>();
    private readonly IInsiderHookService _hooks;
    private readonly IInsiderLogger _logger;
    private readonly object _sync = new object();
    private IDisposable? _pumpHook;
    private bool _disposed;
    private bool _installationAttempted;
    private bool _started;
    private int _mainThreadId;

    public UnityMonoMainThread(IInsiderHookService hooks, IInsiderLogger logger)
    {
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsReady => Volatile.Read(ref _mainThreadId) != 0;

    public bool IsCurrent
    {
        get
        {
            var mainThreadId = Volatile.Read(ref _mainThreadId);
            return mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId;
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_started)
            {
                return;
            }

            _started = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryInstallPump(assembly);
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
            _callbacks.Enqueue(callback);
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
            var registration = new UpdateRegistration(this, callback);
            _updates.Add(registration);
            return registration;
        }
    }

    public void Dispose()
    {
        IDisposable? pumpHook;
        UpdateRegistration[] updates;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            _callbacks.Clear();
            updates = _updates.ToArray();
            _updates.Clear();
            pumpHook = _pumpHook;
            _pumpHook = null;
        }

        foreach (var update in updates)
        {
            update.Dispose();
        }

        try
        {
            pumpHook?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not remove the Unity main-thread pump.", exception);
        }
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs eventArgs)
    {
        TryInstallPump(eventArgs.LoadedAssembly);
    }

    private void TryInstallPump(Assembly assembly)
    {
        if (!string.Equals(assembly.GetName().Name, UnityCoreAssemblyName, StringComparison.Ordinal))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed || _installationAttempted)
            {
                return;
            }

            _installationAttempted = true;
        }

        try
        {
            var synchronizationContextType = assembly.GetType(
                UnitySynchronizationContextTypeName,
                throwOnError: false,
                ignoreCase: false)
                ?? throw new MissingMemberException(
                    UnitySynchronizationContextTypeName,
                    ExecuteTasksMethodName);
            var executeTasks = synchronizationContextType.GetMethod(
                ExecuteTasksMethodName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(
                    UnitySynchronizationContextTypeName,
                    ExecuteTasksMethodName);

            var hook = _hooks.Detour(executeTasks, (ExecuteTasksHook)Pump);
            lock (_sync)
            {
                if (_disposed)
                {
                    hook.Dispose();
                    return;
                }

                _pumpHook = hook;
            }

            _logger.Info("Unity main-thread pump installed.");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not install the Unity main-thread pump.", exception);
        }
    }

    private void Pump(ExecuteTasksOriginal original)
    {
        original();
        Volatile.Write(ref _mainThreadId, Thread.CurrentThread.ManagedThreadId);

        Action[] callbacks;
        UpdateRegistration[] updates;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            callbacks = _callbacks.ToArray();
            _callbacks.Clear();
            updates = _updates.ToArray();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                _logger.Error("A Unity main-thread callback failed.", exception);
            }
        }

        foreach (var update in updates)
        {
            try
            {
                update.Invoke();
            }
            catch (Exception exception)
            {
                _logger.Error("A Unity update callback failed.", exception);
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
            throw new ObjectDisposedException(nameof(UnityMonoMainThread));
        }
    }

    private delegate void ExecuteTasksOriginal();

    private delegate void ExecuteTasksHook(ExecuteTasksOriginal original);

    private sealed class UpdateRegistration : IDisposable
    {
        private readonly Action _callback;
        private readonly UnityMonoMainThread _owner;
        private readonly object _sync = new object();
        private bool _disposed;

        public UpdateRegistration(UnityMonoMainThread owner, Action callback)
        {
            _owner = owner;
            _callback = callback;
        }

        public void Invoke()
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _callback();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _owner.RemoveUpdate(this);
        }
    }
}

using System;
using System.IO;
using Insider.Hooking;
using Insider.Loader;

namespace Insider.Bootstrap;

internal sealed class BootstrapSession : IDisposable
{
    private readonly object _sync = new object();
    private UnityMonoMainThread? _mainThread;
    private PluginHost? _pluginHost;
    private bool _started;
    private bool _stopped;

    internal IInsiderLogger? Logger { get; private set; }

    public BootstrapSessionResult Start(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("A game directory is required.", nameof(gameDirectory));
        }

        lock (_sync)
        {
            if (_started)
            {
                throw new InvalidOperationException("This bootstrap session has already been started.");
            }

            _started = true;

            var normalizedGameDirectory = Path.GetFullPath(gameDirectory);
            var insiderDirectory = Path.Combine(normalizedGameDirectory, "Insider");
            var pluginDirectory = Path.Combine(insiderDirectory, "plugins");
            var configDirectory = Path.Combine(insiderDirectory, "config");
            var dataDirectory = Path.Combine(insiderDirectory, "data");
            var logDirectory = Path.Combine(insiderDirectory, "logs");
            var logPath = Path.Combine(logDirectory, "insider.log");

            Directory.CreateDirectory(pluginDirectory);
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(logDirectory);

            var logger = new FileLogger(logPath);
            Logger = logger;

            var runtime = RuntimeDetector.Detect(normalizedGameDirectory);
            logger.Info($"Insider bootstrap started: {runtime.Backend}, {runtime.OperatingSystem}, {runtime.Architecture}.");

            if (runtime.Backend != InsiderRuntimeBackend.UnityMono)
            {
                logger.Warn($"Runtime backend '{runtime.Backend}' is not supported by this build; no plugins were loaded.");
                return new BootstrapSessionResult(
                    runtime,
                    normalizedGameDirectory,
                    insiderDirectory,
                    pluginDirectory,
                    logPath,
                    isSupported: false,
                    loadedPluginCount: 0,
                    failedPluginCount: 0);
            }

            var hooks = new RuntimeDetourHookService();
            _mainThread = new UnityMonoMainThread(hooks, logger);

            var context = new BootstrapContext(
                normalizedGameDirectory,
                insiderDirectory,
                logger,
                runtime,
                _mainThread,
                hooks);
            _pluginHost = new PluginHost(context);

            var disabledPluginPath = Path.Combine(configDirectory, DisabledPluginList.FileName);
            var disabledPluginIds = DisabledPluginList.Read(disabledPluginPath);
            if (disabledPluginIds.Count > 0)
            {
                logger.Info(
                    $"Read {disabledPluginIds.Count} disabled plugin id(s) from '{disabledPluginPath}'.");
            }

            var results = _pluginHost.LoadDirectory(pluginDirectory, disabledPluginIds);
            _mainThread.Start();
            var loaded = 0;
            var failed = 0;

            foreach (var result in results)
            {
                if (result.Succeeded)
                {
                    loaded++;
                }
                else
                {
                    failed++;
                }
            }

            logger.Info($"Plugin scan completed: {loaded} loaded, {failed} failed.");
            return new BootstrapSessionResult(
                runtime,
                normalizedGameDirectory,
                insiderDirectory,
                pluginDirectory,
                logPath,
                isSupported: true,
                loaded,
                failed);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started || _stopped)
            {
                return;
            }

            _stopped = true;
            try
            {
                _pluginHost?.Dispose();
            }
            finally
            {
                _pluginHost = null;
                _mainThread?.Dispose();
                _mainThread = null;
            }

            Logger?.Info("Insider bootstrap stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

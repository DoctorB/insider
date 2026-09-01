using System;
using System.IO;
using Insider.Hooking;
using Insider.Loader;

namespace Insider.Bootstrap;

internal sealed class BootstrapSession : IDisposable
{
    private readonly object _sync = new object();
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
            var logDirectory = Path.Combine(insiderDirectory, "logs");
            var logPath = Path.Combine(logDirectory, "insider.log");

            Directory.CreateDirectory(pluginDirectory);
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

            var context = new BootstrapContext(
                normalizedGameDirectory,
                insiderDirectory,
                logger,
                runtime,
                new RuntimeDetourHookService());
            _pluginHost = new PluginHost(context);

            var results = _pluginHost.LoadDirectory(pluginDirectory);
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
            _pluginHost?.Dispose();
            _pluginHost = null;
            Logger?.Info("Insider bootstrap stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

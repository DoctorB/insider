using System;
using System.IO;
using System.Threading;
using Insider.Loader;

namespace Insider.Bootstrap;

internal static class Bootstrapper
{
    private static int _started;
    private static PluginHost? _pluginHost;
    private static IInsiderLogger? _logger;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        try
        {
            var gameDirectory = ResolveGameDirectory();
            var insiderDirectory = Path.Combine(gameDirectory, "Insider");
            var pluginDirectory = Path.Combine(insiderDirectory, "plugins");
            var logDirectory = Path.Combine(insiderDirectory, "logs");

            Directory.CreateDirectory(pluginDirectory);
            Directory.CreateDirectory(logDirectory);

            _logger = new FileLogger(Path.Combine(logDirectory, "insider.log"));
            var runtime = RuntimeDetector.Detect(gameDirectory);
            var context = new BootstrapContext(gameDirectory, insiderDirectory, _logger, runtime);

            _logger.Info($"Insider bootstrap started: {runtime.Backend}, {runtime.OperatingSystem}, {runtime.Architecture}.");

            if (runtime.Backend != InsiderRuntimeBackend.UnityMono)
            {
                _logger.Warn($"Runtime backend '{runtime.Backend}' is not supported by this build; no plugins were loaded.");
                return;
            }

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

            _logger.Info($"Plugin scan completed: {loaded} loaded, {failed} failed.");
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        catch (Exception exception)
        {
            if (_logger is not null)
            {
                _logger.Log(InsiderLogLevel.Critical, "Insider bootstrap failed.", exception);
            }
            else
            {
                TryWriteEmergencyLog(exception);
            }
        }
    }

    private static void OnProcessExit(object? sender, EventArgs eventArgs)
    {
        _pluginHost?.UnloadAll();
    }

    private static string ResolveGameDirectory()
    {
        var processPath = Environment.GetEnvironmentVariable("INSIDER_PROCESS_PATH");
        if (string.IsNullOrWhiteSpace(processPath))
        {
            processPath = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(processPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static void TryWriteEmergencyLog(Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "insider-bootstrap-error.log"),
                exception + Environment.NewLine);
        }
        catch
        {
            // The bootstrap must never replace the original failure with a logging failure.
        }
    }
}

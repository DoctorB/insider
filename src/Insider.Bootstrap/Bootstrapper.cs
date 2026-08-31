using System;
using System.IO;
using System.Threading;

namespace Insider.Bootstrap;

internal static class Bootstrapper
{
    private static int _started;
    private static BootstrapSession? _session;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        try
        {
            var session = new BootstrapSession();
            _session = session;
            session.Start(ResolveGameDirectory());
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        catch (Exception exception)
        {
            if (_session?.Logger is not null)
            {
                _session.Logger.Log(InsiderLogLevel.Critical, "Insider bootstrap failed.", exception);
            }
            else
            {
                TryWriteEmergencyLog(exception);
            }
        }
    }

    private static void OnProcessExit(object? sender, EventArgs eventArgs)
    {
        _session?.Stop();
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

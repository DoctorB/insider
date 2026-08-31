using System;

namespace Insider;

public interface IInsiderLogger
{
    void Log(InsiderLogLevel level, string message, Exception? exception = null);
}

public enum InsiderLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

public static class InsiderLoggerExtensions
{
    public static void Info(this IInsiderLogger logger, string message)
    {
        RequireLogger(logger).Log(InsiderLogLevel.Information, message);
    }

    public static void Warn(this IInsiderLogger logger, string message)
    {
        RequireLogger(logger).Log(InsiderLogLevel.Warning, message);
    }

    public static void Error(this IInsiderLogger logger, string message, Exception? exception = null)
    {
        RequireLogger(logger).Log(InsiderLogLevel.Error, message, exception);
    }

    private static IInsiderLogger RequireLogger(IInsiderLogger logger)
    {
        return logger ?? throw new ArgumentNullException(nameof(logger));
    }
}

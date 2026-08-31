using System;

namespace Insider.Loader;

public sealed class PluginLoadResult
{
    private PluginLoadResult(bool succeeded, PluginDescriptor? plugin, string source, string? error, Exception? exception)
    {
        Succeeded = succeeded;
        Plugin = plugin;
        Source = source;
        Error = error;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public PluginDescriptor? Plugin { get; }

    public string Source { get; }

    public string? Error { get; }

    public Exception? Exception { get; }

    internal static PluginLoadResult Success(PluginDescriptor plugin, string source)
    {
        return new PluginLoadResult(true, plugin, source, null, null);
    }

    internal static PluginLoadResult Failure(string source, string error, Exception? exception = null)
    {
        return new PluginLoadResult(false, null, source, error, exception);
    }
}

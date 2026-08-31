using System;

namespace Insider.Loader;

internal sealed class PluginLogger : IInsiderLogger
{
    private readonly IInsiderLogger _inner;
    private readonly string _prefix;

    public PluginLogger(IInsiderLogger inner, string pluginId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("A plugin id is required.", nameof(pluginId));
        }

        _prefix = $"[{pluginId}] ";
    }

    public void Log(InsiderLogLevel level, string message, Exception? exception = null)
    {
        _inner.Log(level, _prefix + message, exception);
    }
}

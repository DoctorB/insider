using System;

namespace Insider.Loader;

internal sealed class PluginContext : IInsiderContext
{
    private readonly IInsiderContext _inner;

    public PluginContext(IInsiderContext inner, string pluginId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Logger = new PluginLogger(inner.Logger, pluginId);
    }

    public string GameDirectory => _inner.GameDirectory;

    public string InsiderDirectory => _inner.InsiderDirectory;

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime => _inner.Runtime;
}

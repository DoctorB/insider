using System;

namespace Insider.Loader;

internal sealed class PluginContext : IInsiderContext, IDisposable
{
    private readonly IInsiderContext _inner;
    private readonly PluginHookService _hooks;

    public PluginContext(IInsiderContext inner, string pluginId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Logger = new PluginLogger(inner.Logger, pluginId);
        _hooks = new PluginHookService(inner.Hooks);
    }

    public string GameDirectory => _inner.GameDirectory;

    public string InsiderDirectory => _inner.InsiderDirectory;

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime => _inner.Runtime;

    public IInsiderHookService Hooks => _hooks;

    public void Dispose()
    {
        _hooks.Dispose();
    }
}

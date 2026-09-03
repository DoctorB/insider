using System.IO;

namespace Insider.Bootstrap;

internal sealed class BootstrapContext : IInsiderContext
{
    public BootstrapContext(
        string gameDirectory,
        string insiderDirectory,
        IInsiderLogger logger,
        IInsiderRuntimeInfo runtime,
        IInsiderMainThread mainThread,
        IInsiderHookService hooks)
    {
        GameDirectory = gameDirectory;
        InsiderDirectory = insiderDirectory;
        PluginDirectory = Path.Combine(insiderDirectory, "plugins");
        ConfigDirectory = Path.Combine(insiderDirectory, "config");
        DataDirectory = Path.Combine(insiderDirectory, "data");
        Logger = logger;
        Runtime = runtime;
        MainThread = mainThread;
        Hooks = hooks;
    }

    public string GameDirectory { get; }

    public string InsiderDirectory { get; }

    public string PluginDirectory { get; }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime { get; }

    public IInsiderMainThread MainThread { get; }

    public IInsiderHookService Hooks { get; }
}

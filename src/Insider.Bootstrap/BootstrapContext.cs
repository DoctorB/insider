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
        Logger = logger;
        Runtime = runtime;
        MainThread = mainThread;
        Hooks = hooks;
    }

    public string GameDirectory { get; }

    public string InsiderDirectory { get; }

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime { get; }

    public IInsiderMainThread MainThread { get; }

    public IInsiderHookService Hooks { get; }
}

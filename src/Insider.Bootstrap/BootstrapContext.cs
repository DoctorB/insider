namespace Insider.Bootstrap;

internal sealed class BootstrapContext : IInsiderContext
{
    public BootstrapContext(
        string gameDirectory,
        string insiderDirectory,
        IInsiderLogger logger,
        IInsiderRuntimeInfo runtime)
    {
        GameDirectory = gameDirectory;
        InsiderDirectory = insiderDirectory;
        Logger = logger;
        Runtime = runtime;
    }

    public string GameDirectory { get; }

    public string InsiderDirectory { get; }

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime { get; }
}

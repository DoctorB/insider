namespace Insider.Bootstrap;

internal sealed class BootstrapSessionResult
{
    public BootstrapSessionResult(
        IInsiderRuntimeInfo runtime,
        string gameDirectory,
        string insiderDirectory,
        string pluginDirectory,
        string logPath,
        bool isSupported,
        int loadedPluginCount,
        int failedPluginCount)
    {
        Runtime = runtime;
        GameDirectory = gameDirectory;
        InsiderDirectory = insiderDirectory;
        PluginDirectory = pluginDirectory;
        LogPath = logPath;
        IsSupported = isSupported;
        LoadedPluginCount = loadedPluginCount;
        FailedPluginCount = failedPluginCount;
    }

    public IInsiderRuntimeInfo Runtime { get; }

    public string GameDirectory { get; }

    public string InsiderDirectory { get; }

    public string PluginDirectory { get; }

    public string LogPath { get; }

    public bool IsSupported { get; }

    public int LoadedPluginCount { get; }

    public int FailedPluginCount { get; }
}

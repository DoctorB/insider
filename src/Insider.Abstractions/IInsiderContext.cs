namespace Insider;

public interface IInsiderContext
{
    string GameDirectory { get; }

    string InsiderDirectory { get; }

    /// <summary>
    /// Gets the directory containing this plugin's entry assembly.
    /// </summary>
    string PluginDirectory { get; }

    /// <summary>
    /// Gets the persistent configuration directory owned by this plugin.
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>
    /// Gets the persistent data directory owned by this plugin.
    /// </summary>
    string DataDirectory { get; }

    IInsiderLogger Logger { get; }

    IInsiderRuntimeInfo Runtime { get; }

    /// <summary>
    /// Gets the IL2CPP runtime bridge when the current game uses IL2CPP;
    /// otherwise <see langword="null"/>.
    /// </summary>
    IInsiderIl2CppRuntime? Il2Cpp { get; }

    IInsiderMainThread MainThread { get; }

    IInsiderHookService Hooks { get; }
}

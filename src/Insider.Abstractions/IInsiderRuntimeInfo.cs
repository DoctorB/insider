namespace Insider;

public interface IInsiderRuntimeInfo
{
    InsiderRuntimeBackend Backend { get; }

    string OperatingSystem { get; }

    string Architecture { get; }

    string RuntimeVersion { get; }

    /// <summary>Gets whether reflected managed detours are supported for game code.</summary>
    bool SupportsManagedDetours { get; }

    /// <summary>Gets whether managed IL rewriting is supported for game code.</summary>
    bool SupportsIlHooks { get; }

    /// <summary>Gets whether process-local native function detours are supported.</summary>
    bool SupportsNativeDetours { get; }

    /// <summary>Gets whether Unity main-thread posting and updates are supported.</summary>
    bool SupportsMainThread { get; }
}

public enum InsiderRuntimeBackend
{
    Unknown,
    UnityMono,
    UnityIl2Cpp,
}

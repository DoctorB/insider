namespace Insider;

public interface IInsiderRuntimeInfo
{
    InsiderRuntimeBackend Backend { get; }

    string OperatingSystem { get; }

    string Architecture { get; }

    string RuntimeVersion { get; }
}

public enum InsiderRuntimeBackend
{
    Unknown,
    UnityMono,
    UnityIl2Cpp,
}

using System;
using System.IO;

namespace Insider.Cli;

internal static class UnityGameInspector
{
    public static UnityGameInspection Inspect(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            return UnityGameInspection.Missing(fullPath);
        }

        var gameDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var gameName = Path.GetFileNameWithoutExtension(fullPath);
        var dataDirectory = Path.Combine(gameDirectory, gameName + "_Data");
        var unityPlayer = Path.Combine(gameDirectory, "UnityPlayer.dll");
        var gameAssembly = Path.Combine(gameDirectory, "GameAssembly.dll");
        var managedDirectory = Path.Combine(dataDirectory, "Managed");
        var il2CppData = Path.Combine(dataDirectory, "il2cpp_data");
        var il2CppMetadata = Path.Combine(il2CppData, "Metadata", "global-metadata.dat");

        var isUnity = Directory.Exists(dataDirectory) &&
            (File.Exists(unityPlayer) || Directory.Exists(managedDirectory) || Directory.Exists(il2CppData));

        var backend = UnityScriptingBackend.Unknown;
        if (File.Exists(gameAssembly) || Directory.Exists(il2CppData))
        {
            backend = UnityScriptingBackend.Il2Cpp;
        }
        else if (Directory.Exists(managedDirectory))
        {
            backend = UnityScriptingBackend.Mono;
        }

        var architecture = PortableExecutableInspector.GetArchitecture(fullPath);
        var hasCompleteIl2CppLayout = File.Exists(gameAssembly) && File.Exists(il2CppMetadata);
        var isCurrentTarget = isUnity && architecture == "x64" &&
            (backend == UnityScriptingBackend.Mono ||
                (backend == UnityScriptingBackend.Il2Cpp && hasCompleteIl2CppLayout));
        var note = isCurrentTarget
            ? backend == UnityScriptingBackend.Il2Cpp
                ? "Matches the essential Windows x64 Unity/IL2CPP target; native APIs and hooks are version-sensitive."
                : "Matches the experimental Windows x64 Unity/Mono target; validate this specific game before use."
            : backend == UnityScriptingBackend.Il2Cpp && !hasCompleteIl2CppLayout
                ? "The IL2CPP layout is incomplete: GameAssembly.dll and global-metadata.dat are both required."
                : "Detection is diagnostic only. This configuration is outside the current implementation target.";

        return new UnityGameInspection(fullPath, dataDirectory, isUnity, backend, architecture, isCurrentTarget, note);
    }
}

internal enum UnityScriptingBackend
{
    Unknown,
    Mono,
    Il2Cpp,
}

internal sealed class UnityGameInspection
{
    public UnityGameInspection(
        string executablePath,
        string dataDirectory,
        bool isUnityGame,
        UnityScriptingBackend backend,
        string architecture,
        bool isCurrentTarget,
        string note)
    {
        ExecutablePath = executablePath;
        DataDirectory = dataDirectory;
        IsUnityGame = isUnityGame;
        Backend = backend;
        Architecture = architecture;
        IsCurrentTarget = isCurrentTarget;
        Note = note;
    }

    public string ExecutablePath { get; }

    public string DataDirectory { get; }

    public bool IsUnityGame { get; }

    public UnityScriptingBackend Backend { get; }

    public string Architecture { get; }

    public bool IsCurrentTarget { get; }

    public string Note { get; }

    public static UnityGameInspection Missing(string path)
    {
        return new UnityGameInspection(path, string.Empty, false, UnityScriptingBackend.Unknown, "Unknown", false, "Executable not found.");
    }
}

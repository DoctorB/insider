using System;
using System.IO;

namespace Insider.Bootstrap;

internal static class RuntimeDetector
{
    public static IInsiderRuntimeInfo Detect(string gameDirectory)
    {
        var backend = DetectBackend(gameDirectory);
        var architecture = IntPtr.Size == 8 ? "x64" : "x86";
        var operatingSystem = Environment.OSVersion.Platform.ToString();
        return new RuntimeInfo(backend, operatingSystem, architecture, Environment.Version.ToString());
    }

    private static InsiderRuntimeBackend DetectBackend(string gameDirectory)
    {
        if (File.Exists(Path.Combine(gameDirectory, "GameAssembly.dll")))
        {
            return InsiderRuntimeBackend.UnityIl2Cpp;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOORSTOP_MONO_LIB_PATH")))
        {
            return InsiderRuntimeBackend.UnityMono;
        }

        if (Directory.Exists(Path.Combine(gameDirectory, "Mono")) ||
            Directory.Exists(Path.Combine(gameDirectory, "MonoBleedingEdge")))
        {
            return InsiderRuntimeBackend.UnityMono;
        }

        return InsiderRuntimeBackend.Unknown;
    }

    private sealed class RuntimeInfo : IInsiderRuntimeInfo
    {
        public RuntimeInfo(
            InsiderRuntimeBackend backend,
            string operatingSystem,
            string architecture,
            string runtimeVersion)
        {
            Backend = backend;
            OperatingSystem = operatingSystem;
            Architecture = architecture;
            RuntimeVersion = runtimeVersion;
        }

        public InsiderRuntimeBackend Backend { get; }

        public string OperatingSystem { get; }

        public string Architecture { get; }

        public string RuntimeVersion { get; }
    }
}

using System;
using System.IO;
using Insider;

namespace Insider.UnityMonoSmokePlugin;

[InsiderPlugin("dev.insider.tests.unity-mono-smoke", "Unity Mono Smoke", "1.0.0")]
public sealed class UnityMonoSmokePlugin : IInsiderPlugin
{
    public const string Marker = "INSIDER_UNITY_MONO_SMOKE_PLUGIN_LOADED";

    private string? _insiderDirectory;

    public void Load(IInsiderContext context)
    {
        _insiderDirectory = context.InsiderDirectory;
        context.Logger.Info(Marker);
        File.WriteAllText(
            Path.Combine(context.InsiderDirectory, "unity-smoke-plugin-loaded.txt"),
            $"Backend={context.Runtime.Backend}{Environment.NewLine}" +
            $"Architecture={context.Runtime.Architecture}{Environment.NewLine}" +
            $"GameDirectory={context.GameDirectory}{Environment.NewLine}");
    }

    public void Unload()
    {
        if (_insiderDirectory is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(_insiderDirectory, "unity-smoke-plugin-unloaded.txt"),
            "unloaded");
    }
}

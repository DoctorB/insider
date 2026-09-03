using System.IO;
using Insider.DependencyFixture;

namespace Insider.PluginFixture;

[InsiderPlugin("dev.insider.tests.bootstrap-fixture", "Bootstrap Fixture", "1.0.0")]
public sealed class BootstrapFixturePlugin : IInsiderPlugin
{
    private string? _insiderDirectory;

    public void Load(IInsiderContext context)
    {
        _insiderDirectory = context.InsiderDirectory;
        context.Logger.Info("Bootstrap fixture loaded.");
        File.WriteAllText(
            Path.Combine(_insiderDirectory, "fixture-loaded.txt"),
            $"Backend={context.Runtime.Backend}{System.Environment.NewLine}" +
            $"GameDirectory={context.GameDirectory}{System.Environment.NewLine}" +
            $"PluginDirectory={context.PluginDirectory}{System.Environment.NewLine}" +
            $"ConfigDirectory={context.ConfigDirectory}{System.Environment.NewLine}" +
            $"DataDirectory={context.DataDirectory}{System.Environment.NewLine}" +
            $"Dependency={DependencyValue.Current}");
    }

    public void Unload()
    {
        if (_insiderDirectory is null)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_insiderDirectory, "fixture-unloaded.txt"), "unloaded");
    }
}

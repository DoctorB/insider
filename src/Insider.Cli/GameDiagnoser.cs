using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Insider.Installation;

namespace Insider.Cli;

internal static class GameDiagnoser
{
    public static GameDiagnosticReport Diagnose(string executablePath)
    {
        var inspection = UnityGameInspector.Inspect(executablePath);
        var installation = new InsiderInstaller().GetStatus(inspection.ExecutablePath);
        var problems = new List<string>();
        var notes = new List<string>();

        if (!File.Exists(inspection.ExecutablePath))
        {
            problems.Add($"Game executable not found: '{inspection.ExecutablePath}'.");
        }
        else if (!inspection.IsUnityGame)
        {
            problems.Add("The executable does not have a recognizable Unity player layout.");
        }
        else if (!inspection.IsCurrentTarget)
        {
            problems.Add(
                $"The detected {inspection.Backend}/{inspection.Architecture} player is outside the current Windows x64 Unity Mono or IL2CPP targets.");
        }

        if (installation.State == InsiderInstallationState.NotInstalled)
        {
            problems.Add("Insider is not installed for this game.");
        }
        else if (installation.State == InsiderInstallationState.Damaged)
        {
            problems.AddRange(installation.Issues.Select(issue => $"Installation: {issue}"));
        }

        var pluginDirectory = Path.Combine(installation.GameDirectory, "Insider", "plugins");
        var coreDirectory = Path.Combine(installation.GameDirectory, "Insider", "core");
        IReadOnlyList<string> disabledPluginIds = Array.Empty<string>();
        if (File.Exists(inspection.ExecutablePath) && installation.State != InsiderInstallationState.NotInstalled)
        {
            try
            {
                disabledPluginIds = new DisabledPluginManager().GetDisabled(inspection.ExecutablePath);
            }
            catch (InsiderInstallationException exception)
            {
                problems.Add($"Disabled-plugin list: {exception.Message}");
            }
        }

        var pluginReport = PluginDirectoryDiagnoser.Inspect(pluginDirectory, coreDirectory, disabledPluginIds);
        problems.AddRange(pluginReport.Problems);
        notes.AddRange(pluginReport.Notes);

        return new GameDiagnosticReport(
            inspection,
            installation,
            pluginDirectory,
            disabledPluginIds,
            pluginReport.Plugins,
            problems.AsReadOnly(),
            notes.AsReadOnly());
    }
}

internal sealed class GameDiagnosticReport
{
    public GameDiagnosticReport(
        UnityGameInspection inspection,
        InsiderInstallationStatus installation,
        string pluginDirectory,
        IReadOnlyList<string> disabledPluginIds,
        IReadOnlyList<PluginDiagnostic> plugins,
        IReadOnlyList<string> problems,
        IReadOnlyList<string> notes)
    {
        Inspection = inspection;
        Installation = installation;
        PluginDirectory = pluginDirectory;
        DisabledPluginIds = disabledPluginIds;
        Plugins = plugins;
        Problems = problems;
        Notes = notes;
    }

    public UnityGameInspection Inspection { get; }

    public InsiderInstallationStatus Installation { get; }

    public string PluginDirectory { get; }

    public IReadOnlyList<string> DisabledPluginIds { get; }

    public IReadOnlyList<PluginDiagnostic> Plugins { get; }

    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyList<string> Notes { get; }

    public bool HasProblems => Problems.Count > 0;
}

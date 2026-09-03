using System;
using System.IO;
using Insider.Installation;

namespace Insider.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (InsiderInstallationException exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unexpected error: {exception.Message}");
            return 1;
        }
    }

    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "diagnose" => RunDiagnose(args),
            "inspect" => RunInspect(args),
            "install" => RunInstall(args),
            "status" => RunStatus(args),
            "uninstall" => RunUninstall(args),
            "plugins" => RunPlugins(args),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int RunDiagnose(string[] args)
    {
        if (args.Length != 2)
        {
            throw new InsiderInstallationException("Usage: insider diagnose <path-to-game-executable>");
        }

        var report = GameDiagnoser.Diagnose(args[1]);
        PrintDiagnosis(report);
        return report.HasProblems ? 1 : 0;
    }

    private static int RunInspect(string[] args)
    {
        if (args.Length != 2)
        {
            throw new InsiderInstallationException("Usage: insider inspect <path-to-game-executable>");
        }

        var result = UnityGameInspector.Inspect(args[1]);
        PrintInspection(result);
        return result.IsUnityGame ? 0 : 1;
    }

    private static int RunInstall(string[] args)
    {
        if (args.Length is not 2 and not 4)
        {
            throw new InsiderInstallationException(
                "Usage: insider install <path-to-game-executable> [--bundle <bundle-directory>]");
        }

        var bundleDirectory = Path.Combine(AppContext.BaseDirectory, "bundle");
        if (args.Length == 4)
        {
            if (!args[2].Equals("--bundle", StringComparison.OrdinalIgnoreCase))
            {
                throw new InsiderInstallationException($"Unknown install option '{args[2]}'.");
            }

            bundleDirectory = args[3];
        }

        var inspection = UnityGameInspector.Inspect(args[1]);
        PrintInspection(inspection);
        if (!inspection.IsCurrentTarget)
        {
            throw new InsiderInstallationException(
                "Installation is limited to complete Windows x64 Unity Mono or IL2CPP game layouts.");
        }

        var status = new InsiderInstaller().Install(inspection.ExecutablePath, bundleDirectory);
        Console.WriteLine();
        Console.WriteLine($"Insider installed in: {status.GameDirectory}");
        Console.WriteLine("Place managed plugins in: Insider/plugins");
        return 0;
    }

    private static int RunStatus(string[] args)
    {
        if (args.Length != 2)
        {
            throw new InsiderInstallationException("Usage: insider status <path-to-game-executable>");
        }

        var status = new InsiderInstaller().GetStatus(args[1]);
        PrintStatus(status);
        return status.State == InsiderInstallationState.Damaged ? 1 : 0;
    }

    private static int RunUninstall(string[] args)
    {
        if (args.Length is not 2 and not 3)
        {
            throw new InsiderInstallationException(
                "Usage: insider uninstall <path-to-game-executable> [--force]");
        }

        var force = args.Length == 3;
        if (force && !args[2].Equals("--force", StringComparison.OrdinalIgnoreCase))
        {
            throw new InsiderInstallationException($"Unknown uninstall option '{args[2]}'.");
        }

        var status = new InsiderInstaller().Uninstall(args[1], force);
        Console.WriteLine($"Insider is not installed in: {status.GameDirectory}");
        if (force && status.Issues.Count > 0)
        {
            Console.WriteLine("Modified files were removed because --force was specified.");
        }

        return 0;
    }

    private static int RunPlugins(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InsiderInstallationException(
                "Usage: insider plugins <disable|enable|disabled> <path-to-game-executable> [plugin-id]");
        }

        var manager = new DisabledPluginManager();
        return args[1].ToLowerInvariant() switch
        {
            "disable" => RunPluginDisable(args, manager),
            "enable" => RunPluginEnable(args, manager),
            "disabled" => RunPluginsDisabled(args, manager),
            _ => throw new InsiderInstallationException(
                $"Unknown plugins command '{args[1]}'. Expected disable, enable, or disabled."),
        };
    }

    private static int RunPluginDisable(string[] args, DisabledPluginManager manager)
    {
        if (args.Length != 4)
        {
            throw new InsiderInstallationException(
                "Usage: insider plugins disable <path-to-game-executable> <plugin-id>");
        }

        var changed = manager.Disable(args[2], args[3]);
        Console.WriteLine(changed
            ? $"Disabled plugin '{args[3].Trim()}'. Restart the game to apply the change."
            : $"Plugin '{args[3].Trim()}' is already disabled.");
        return 0;
    }

    private static int RunPluginEnable(string[] args, DisabledPluginManager manager)
    {
        if (args.Length != 4)
        {
            throw new InsiderInstallationException(
                "Usage: insider plugins enable <path-to-game-executable> <plugin-id>");
        }

        var changed = manager.Enable(args[2], args[3]);
        Console.WriteLine(changed
            ? $"Enabled plugin '{args[3].Trim()}'. Restart the game to apply the change."
            : $"Plugin '{args[3].Trim()}' is not disabled.");
        return 0;
    }

    private static int RunPluginsDisabled(string[] args, DisabledPluginManager manager)
    {
        if (args.Length != 3)
        {
            throw new InsiderInstallationException(
                "Usage: insider plugins disabled <path-to-game-executable>");
        }

        var disabled = manager.GetDisabled(args[2]);
        if (disabled.Count == 0)
        {
            Console.WriteLine("No plugins are disabled.");
            return 0;
        }

        Console.WriteLine($"Disabled plugins ({disabled.Count}):");
        foreach (var pluginId in disabled)
        {
            Console.WriteLine($"  {pluginId}");
        }

        return 0;
    }

    private static void PrintInspection(UnityGameInspection result)
    {
        Console.WriteLine($"Executable:   {result.ExecutablePath}");
        Console.WriteLine($"Unity layout: {result.IsUnityGame}");
        Console.WriteLine($"Backend:      {result.Backend}");
        Console.WriteLine($"Architecture: {result.Architecture}");
        Console.WriteLine($"Data path:    {result.DataDirectory}");
        Console.WriteLine($"Current target: {result.IsCurrentTarget}");

        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            Console.WriteLine($"Note:         {result.Note}");
        }
    }

    private static void PrintStatus(InsiderInstallationStatus status)
    {
        Console.WriteLine($"Game path: {status.GameDirectory}");
        Console.WriteLine($"Insider:   {status.State}");
        foreach (var issue in status.Issues)
        {
            Console.WriteLine($"Issue:     {issue}");
        }
    }

    private static void PrintDiagnosis(GameDiagnosticReport report)
    {
        Console.WriteLine("Insider diagnostics");
        Console.WriteLine();
        Console.WriteLine("Game");
        Console.WriteLine($"  Executable:   {report.Inspection.ExecutablePath}");
        Console.WriteLine($"  Unity layout: {report.Inspection.IsUnityGame}");
        Console.WriteLine($"  Backend:      {report.Inspection.Backend}");
        Console.WriteLine($"  Architecture: {report.Inspection.Architecture}");
        Console.WriteLine($"  Supported:    {report.Inspection.IsCurrentTarget}");
        Console.WriteLine();
        Console.WriteLine("Installation");
        Console.WriteLine($"  State:        {report.Installation.State}");
        Console.WriteLine($"  Directory:    {report.Installation.GameDirectory}");
        Console.WriteLine();
        Console.WriteLine("Plugins");
        Console.WriteLine($"  Directory:    {report.PluginDirectory}");
        Console.WriteLine($"  Found:        {report.Plugins.Count}");
        Console.WriteLine($"  Disabled IDs: {report.DisabledPluginIds.Count}");

        if (report.Plugins.Count == 0)
        {
            Console.WriteLine("  No Insider plugins found.");
        }

        foreach (var plugin in report.Plugins)
        {
            Console.WriteLine($"  [{plugin.State}] {plugin.Id} {plugin.Version} - {plugin.Name}");
            Console.WriteLine($"    Assembly: {plugin.AssemblyPath}");
            if (plugin.MinimumInsiderVersion is not null)
            {
                Console.WriteLine($"    Insider:  >= {plugin.MinimumInsiderVersion}");
            }

            foreach (var dependency in plugin.Dependencies)
            {
                var requirement = dependency.MinimumVersion is null
                    ? dependency.Id
                    : $"{dependency.Id} >= {dependency.MinimumVersion}";
                var kind = dependency.Optional ? "optional" : "required";
                Console.WriteLine($"    Dependency: {requirement} ({kind}) - {dependency.Status}");
            }

            foreach (var issue in plugin.Issues)
            {
                Console.WriteLine($"    Problem: {issue}");
            }
        }

        if (report.DisabledPluginIds.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Disabled plugin IDs");
            foreach (var pluginId in report.DisabledPluginIds)
            {
                Console.WriteLine($"  {pluginId}");
            }
        }

        if (report.Notes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Notes");
            foreach (var note in report.Notes)
            {
                Console.WriteLine($"  {note}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Problems ({report.Problems.Count})");
        if (report.Problems.Count == 0)
        {
            Console.WriteLine("  None. The detected configuration is ready for the current Insider build.");
        }
        else
        {
            foreach (var problem in report.Problems)
            {
                Console.WriteLine($"  {problem}");
            }
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Insider Mod Loader CLI (pre-alpha)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  diagnose <game.exe>                        Check game, installation, plugins, and dependencies.");
        Console.WriteLine("  inspect <game.exe>                         Inspect Unity backend and architecture.");
        Console.WriteLine("  install <game.exe> [--bundle <directory>] Install the Windows x64 bundle.");
        Console.WriteLine("  status <game.exe>                          Verify installed files and hashes.");
        Console.WriteLine("  uninstall <game.exe> [--force]             Remove Insider and restore version.dll.");
        Console.WriteLine("  plugins disable <game.exe> <plugin-id>      Disable a plugin on the next game start.");
        Console.WriteLine("  plugins enable <game.exe> <plugin-id>       Enable a plugin on the next game start.");
        Console.WriteLine("  plugins disabled <game.exe>                 List disabled plugin IDs.");
    }
}

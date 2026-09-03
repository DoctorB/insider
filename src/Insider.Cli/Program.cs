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
            "inspect" => RunInspect(args),
            "install" => RunInstall(args),
            "status" => RunStatus(args),
            "uninstall" => RunUninstall(args),
            "plugins" => RunPlugins(args),
            _ => UnknownCommand(args[0]),
        };
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
                "Installation is currently limited to Windows x64 Unity games using the Mono scripting backend.");
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
        Console.WriteLine($"MVP support:  {result.IsCurrentTarget}");

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
        Console.WriteLine("  inspect <game.exe>                         Inspect Unity backend and architecture.");
        Console.WriteLine("  install <game.exe> [--bundle <directory>] Install the Windows x64 bundle.");
        Console.WriteLine("  status <game.exe>                          Verify installed files and hashes.");
        Console.WriteLine("  uninstall <game.exe> [--force]             Remove Insider and restore version.dll.");
        Console.WriteLine("  plugins disable <game.exe> <plugin-id>      Disable a plugin on the next game start.");
        Console.WriteLine("  plugins enable <game.exe> <plugin-id>       Enable a plugin on the next game start.");
        Console.WriteLine("  plugins disabled <game.exe>                 List disabled plugin IDs.");
    }
}

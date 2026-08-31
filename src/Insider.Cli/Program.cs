using System;

namespace Insider.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: insider inspect <path-to-game-executable>");
                return 2;
            }

            return Inspect(args[1]);
        }

        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        PrintHelp();
        return 2;
    }

    private static int Inspect(string executablePath)
    {
        var result = UnityGameInspector.Inspect(executablePath);
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

        return result.IsUnityGame ? 0 : 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Insider Mod Loader CLI (pre-alpha)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  inspect <game.exe>  Detect the Unity backend and process architecture.");
        Console.WriteLine();
        Console.WriteLine("Installation is intentionally unavailable until the native bootstrap bundle is versioned and verified.");
    }
}

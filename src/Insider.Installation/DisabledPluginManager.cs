using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Insider.Installation;

public sealed class DisabledPluginManager
{
    public const string RelativePath = "Insider/config/disabled-plugins.txt";

    private readonly InsiderInstaller _installer = new InsiderInstaller();

    public IReadOnlyList<string> GetDisabled(string gameExecutable)
    {
        var path = ResolveListPath(gameExecutable);

        try
        {
            return ReadDisabled(File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>());
        }
        catch (Exception exception) when (exception is not InsiderInstallationException)
        {
            throw new InsiderInstallationException($"Could not read the disabled-plugin list at '{path}'.", exception);
        }
    }

    public bool Disable(string gameExecutable, string pluginId)
    {
        var normalizedId = NormalizePluginId(pluginId);
        var path = ResolveListPath(gameExecutable);

        try
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            if (ReadDisabled(lines).Contains(normalizedId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            lines.Add(normalizedId);
            WriteAtomically(path, lines);
            return true;
        }
        catch (Exception exception) when (exception is not InsiderInstallationException)
        {
            throw new InsiderInstallationException($"Could not disable plugin '{normalizedId}'.", exception);
        }
    }

    public bool Enable(string gameExecutable, string pluginId)
    {
        var normalizedId = NormalizePluginId(pluginId);
        var path = ResolveListPath(gameExecutable);

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var lines = File.ReadAllLines(path);
            var remaining = lines
                .Where(line => !IsPluginId(line, normalizedId))
                .ToArray();

            if (remaining.Length == lines.Length)
            {
                return false;
            }

            WriteAtomically(path, remaining);
            return true;
        }
        catch (Exception exception) when (exception is not InsiderInstallationException)
        {
            throw new InsiderInstallationException($"Could not enable plugin '{normalizedId}'.", exception);
        }
    }

    private string ResolveListPath(string gameExecutable)
    {
        if (string.IsNullOrWhiteSpace(gameExecutable))
        {
            throw new InsiderInstallationException("A game executable path is required.");
        }

        string gamePath;
        try
        {
            gamePath = Path.GetFullPath(gameExecutable);
        }
        catch (Exception exception)
        {
            throw new InsiderInstallationException($"Invalid game executable path '{gameExecutable}'.", exception);
        }

        if (!File.Exists(gamePath))
        {
            throw new InsiderInstallationException($"Game executable not found: '{gamePath}'.");
        }

        var status = _installer.GetStatus(gamePath);
        if (status.State == InsiderInstallationState.NotInstalled)
        {
            throw new InsiderInstallationException($"Insider is not installed in: '{status.GameDirectory}'.");
        }

        return Path.Combine(status.GameDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyList<string> ReadDisabled(IEnumerable<string> lines)
    {
        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPluginId(string line, string pluginId)
    {
        var candidate = line.Trim();
        return candidate.Length > 0 &&
            !candidate.StartsWith("#", StringComparison.Ordinal) &&
            string.Equals(candidate, pluginId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePluginId(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new InsiderInstallationException("A plugin ID is required.");
        }

        var normalized = pluginId.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal) ||
            normalized.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            throw new InsiderInstallationException($"Invalid plugin ID '{pluginId}'.");
        }

        return normalized;
    }

    private static void WriteAtomically(string path, IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InsiderInstallationException("The disabled-plugin list has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".new";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }

                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

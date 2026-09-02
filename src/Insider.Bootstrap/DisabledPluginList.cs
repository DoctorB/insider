using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Insider.Bootstrap;

internal static class DisabledPluginList
{
    public const string FileName = "disabled-plugins.txt";

    public static IReadOnlyCollection<string> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A disabled-plugin list path is required.", nameof(path));
        }

        var normalizedPath = Path.GetFullPath(path);
        if (!File.Exists(normalizedPath))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllLines(normalizedPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

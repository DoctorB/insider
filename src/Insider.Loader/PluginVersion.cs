using System;
using System.Globalization;

namespace Insider.Loader;

internal readonly struct PluginVersion : IComparable<PluginVersion>
{
    private PluginVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public static bool TryParse(string? value, out PluginVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value!.Split('.');
        if (parts.Length != 3 ||
            !TryParsePart(parts[0], out var major) ||
            !TryParsePart(parts[1], out var minor) ||
            !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new PluginVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(PluginVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);
    }

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        if (part.Length == 0 || (part.Length > 1 && part[0] == '0'))
        {
            return false;
        }

        return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

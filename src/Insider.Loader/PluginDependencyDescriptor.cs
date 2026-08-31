namespace Insider.Loader;

public sealed class PluginDependencyDescriptor
{
    internal PluginDependencyDescriptor(
        string id,
        string? minimumVersion,
        PluginVersion? parsedMinimumVersion,
        bool optional)
    {
        Id = id;
        MinimumVersion = minimumVersion;
        ParsedMinimumVersion = parsedMinimumVersion;
        Optional = optional;
    }

    public string Id { get; }

    public string? MinimumVersion { get; }

    public bool Optional { get; }

    internal PluginVersion? ParsedMinimumVersion { get; }
}

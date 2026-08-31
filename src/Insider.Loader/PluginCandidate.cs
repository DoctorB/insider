using System;
using System.Collections.Generic;

namespace Insider.Loader;

internal sealed class PluginCandidate
{
    public PluginCandidate(
        Type type,
        InsiderPluginAttribute metadata,
        PluginVersion version,
        IReadOnlyList<PluginDependencyDescriptor> dependencies,
        string source)
    {
        Type = type;
        Metadata = metadata;
        Version = version;
        Dependencies = dependencies;
        Source = source;
    }

    public Type Type { get; }

    public InsiderPluginAttribute Metadata { get; }

    public PluginVersion Version { get; }

    public IReadOnlyList<PluginDependencyDescriptor> Dependencies { get; }

    public string Source { get; }
}

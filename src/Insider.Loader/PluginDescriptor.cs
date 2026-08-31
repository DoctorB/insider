using System.Collections.Generic;

namespace Insider.Loader;

public sealed class PluginDescriptor
{
    internal PluginDescriptor(
        string id,
        string name,
        string version,
        string typeName,
        IReadOnlyList<PluginDependencyDescriptor> dependencies)
    {
        Id = id;
        Name = name;
        Version = version;
        TypeName = typeName;
        Dependencies = dependencies;
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string TypeName { get; }

    public IReadOnlyList<PluginDependencyDescriptor> Dependencies { get; }
}

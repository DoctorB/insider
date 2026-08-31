namespace Insider.Loader;

public sealed class PluginDependencyDescriptor
{
    internal PluginDependencyDescriptor(string id, bool optional)
    {
        Id = id;
        Optional = optional;
    }

    public string Id { get; }

    public bool Optional { get; }
}

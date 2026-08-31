namespace Insider.Loader;

public sealed class PluginDescriptor
{
    internal PluginDescriptor(string id, string name, string version, string typeName)
    {
        Id = id;
        Name = name;
        Version = version;
        TypeName = typeName;
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string TypeName { get; }
}

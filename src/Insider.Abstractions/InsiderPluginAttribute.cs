using System;

namespace Insider;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InsiderPluginAttribute : Attribute
{
    public InsiderPluginAttribute(string id, string name, string version)
    {
        Id = RequireValue(id, nameof(id));
        Name = RequireValue(name, nameof(name));
        Version = RequireValue(version, nameof(version));
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

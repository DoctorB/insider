using System;

namespace Insider;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InsiderPluginDependencyAttribute : Attribute
{
    public InsiderPluginDependencyAttribute(string id, bool optional = false)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("The plugin dependency id cannot be empty.", nameof(id));
        }

        Id = id.Trim();
        Optional = optional;
    }

    public InsiderPluginDependencyAttribute(string id, string minimumVersion, bool optional = false)
        : this(id, optional)
    {
        MinimumVersion = RequireValue(minimumVersion, nameof(minimumVersion));
    }

    public string Id { get; }

    public string? MinimumVersion { get; }

    public bool Optional { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

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

    public string Id { get; }

    public bool Optional { get; }
}

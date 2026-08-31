using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Insider.Loader;

public sealed class PluginHost
{
    private readonly IInsiderContext _context;
    private readonly Dictionary<string, LoadedPlugin> _plugins =
        new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _loadOrder = new List<string>();

    public PluginHost(IInsiderContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IReadOnlyCollection<PluginDescriptor> LoadedPlugins
    {
        get { return _loadOrder.Select(id => _plugins[id].Descriptor).ToArray(); }
    }

    public IReadOnlyList<PluginLoadResult> LoadDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A plugin directory is required.", nameof(directory));
        }

        Directory.CreateDirectory(directory);

        var results = new List<PluginLoadResult>();
        foreach (var assemblyPath in Directory.GetFiles(directory, "*.dll").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            results.AddRange(LoadAssembly(assemblyPath));
        }

        return results.AsReadOnly();
    }

    public IReadOnlyList<PluginLoadResult> LoadAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("An assembly path is required.", nameof(assemblyPath));
        }

        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            return GetLoadableTypes(assembly)
                .Where(IsPluginType)
                .Select(Load)
                .ToArray();
        }
        catch (Exception exception)
        {
            _context.Logger.Error($"Could not load plugin assembly '{assemblyPath}'.", exception);
            return new[] { PluginLoadResult.Failure(assemblyPath, exception.Message, exception) };
        }
    }

    public PluginLoadResult Load(Type pluginType)
    {
        if (pluginType is null)
        {
            throw new ArgumentNullException(nameof(pluginType));
        }

        var source = pluginType.AssemblyQualifiedName ?? pluginType.FullName ?? pluginType.Name;

        if (!IsPluginType(pluginType))
        {
            return Fail(source, $"Type '{pluginType.FullName}' is not a concrete {nameof(IInsiderPlugin)} implementation.");
        }

        var metadata = pluginType.GetCustomAttribute<InsiderPluginAttribute>(inherit: false);
        if (metadata is null)
        {
            return Fail(source, $"Plugin type '{pluginType.FullName}' is missing {nameof(InsiderPluginAttribute)}.");
        }

        if (_plugins.ContainsKey(metadata.Id))
        {
            return Fail(source, $"A plugin with id '{metadata.Id}' is already loaded.");
        }

        IInsiderPlugin? instance = null;
        try
        {
            instance = (IInsiderPlugin?)Activator.CreateInstance(pluginType);
            if (instance is null)
            {
                return Fail(source, $"Plugin type '{pluginType.FullName}' could not be instantiated.");
            }

            var descriptor = new PluginDescriptor(metadata.Id, metadata.Name, metadata.Version, pluginType.FullName ?? pluginType.Name);
            instance.Load(_context);

            _plugins.Add(descriptor.Id, new LoadedPlugin(descriptor, instance));
            _loadOrder.Add(descriptor.Id);
            _context.Logger.Info($"Loaded plugin {descriptor.Id} {descriptor.Version}.");
            return PluginLoadResult.Success(descriptor, source);
        }
        catch (Exception exception)
        {
            TryUnloadPartial(instance, source);
            _context.Logger.Error($"Plugin '{source}' failed during load.", exception);
            return PluginLoadResult.Failure(source, exception.Message, exception);
        }
    }

    public void UnloadAll()
    {
        for (var index = _loadOrder.Count - 1; index >= 0; index--)
        {
            var id = _loadOrder[index];
            var plugin = _plugins[id];

            try
            {
                plugin.Instance.Unload();
                _context.Logger.Info($"Unloaded plugin {id}.");
            }
            catch (Exception exception)
            {
                _context.Logger.Error($"Plugin '{id}' failed during unload.", exception);
            }
        }

        _plugins.Clear();
        _loadOrder.Clear();
    }

    private PluginLoadResult Fail(string source, string error)
    {
        _context.Logger.Warn(error);
        return PluginLoadResult.Failure(source, error);
    }

    private void TryUnloadPartial(IInsiderPlugin? instance, string source)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            instance.Unload();
        }
        catch (Exception exception)
        {
            _context.Logger.Error($"Partially loaded plugin '{source}' also failed during cleanup.", exception);
        }
    }

    private static bool IsPluginType(Type type)
    {
        return typeof(IInsiderPlugin).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>();
        }
    }

    private sealed class LoadedPlugin
    {
        public LoadedPlugin(PluginDescriptor descriptor, IInsiderPlugin instance)
        {
            Descriptor = descriptor;
            Instance = instance;
        }

        public PluginDescriptor Descriptor { get; }

        public IInsiderPlugin Instance { get; }
    }
}

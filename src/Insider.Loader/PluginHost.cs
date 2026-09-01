using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Insider.Loader;

public sealed class PluginHost : IDisposable
{
    private readonly IInsiderContext _context;
    private readonly Dictionary<string, LoadedPlugin> _plugins =
        new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _loadOrder = new List<string>();
    private PluginAssemblyResolver? _dependencyResolver;

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

        var normalizedDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(normalizedDirectory);

        try
        {
            EnsureDependencyResolver(normalizedDirectory);
        }
        catch (Exception exception)
        {
            _context.Logger.Error($"Could not create the plugin dependency catalog for '{normalizedDirectory}'.", exception);
            return new[] { PluginLoadResult.Failure(normalizedDirectory, exception.Message, exception) };
        }

        var results = new List<PluginLoadResult>();
        var pluginTypes = new List<Type>();
        foreach (var assemblyPath in Directory.GetFiles(normalizedDirectory, "*.dll").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            DiscoverAssembly(assemblyPath, pluginTypes, results);
        }

        return LoadTypes(pluginTypes, results);
    }

    public IReadOnlyList<PluginLoadResult> LoadAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("An assembly path is required.", nameof(assemblyPath));
        }

        var results = new List<PluginLoadResult>();
        var pluginTypes = new List<Type>();
        DiscoverAssembly(assemblyPath, pluginTypes, results);
        return LoadTypes(pluginTypes, results);
    }

    public PluginLoadResult Load(Type pluginType)
    {
        if (pluginType is null)
        {
            throw new ArgumentNullException(nameof(pluginType));
        }

        return Load(new[] { pluginType })[0];
    }

    public IReadOnlyList<PluginLoadResult> Load(IEnumerable<Type> pluginTypes)
    {
        if (pluginTypes is null)
        {
            throw new ArgumentNullException(nameof(pluginTypes));
        }

        return LoadTypes(pluginTypes, new List<PluginLoadResult>());
    }

    private IReadOnlyList<PluginLoadResult> LoadTypes(
        IEnumerable<Type> pluginTypes,
        List<PluginLoadResult> results)
    {
        var candidates = new List<PluginCandidate>();
        foreach (var pluginType in pluginTypes)
        {
            if (pluginType is null)
            {
                results.Add(Fail("<null>", "A plugin type cannot be null."));
                continue;
            }

            var candidate = CreateCandidate(pluginType, results);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        var remaining = SelectUniqueCandidates(candidates, results);
        RemoveCandidatesWithMissingDependencies(remaining, results);
        var loadOrder = CreateLoadOrder(remaining, results);

        foreach (var candidate in loadOrder)
        {
            results.Add(Activate(candidate));
        }

        return results.AsReadOnly();
    }

    private PluginCandidate? CreateCandidate(Type pluginType, ICollection<PluginLoadResult> results)
    {
        var source = pluginType.AssemblyQualifiedName ?? pluginType.FullName ?? pluginType.Name;

        if (!IsPluginType(pluginType))
        {
            results.Add(Fail(source, $"Type '{pluginType.FullName}' is not a concrete {nameof(IInsiderPlugin)} implementation."));
            return null;
        }

        var metadata = pluginType.GetCustomAttribute<InsiderPluginAttribute>(inherit: false);
        if (metadata is null)
        {
            results.Add(Fail(source, $"Plugin type '{pluginType.FullName}' is missing {nameof(InsiderPluginAttribute)}."));
            return null;
        }

        if (!PluginVersion.TryParse(metadata.Version, out var pluginVersion))
        {
            results.Add(Fail(
                source,
                $"Plugin '{metadata.Id}' has invalid version '{metadata.Version}'. Expected MAJOR.MINOR.PATCH using non-negative integers."));
            return null;
        }

        var dependencyAttributes = pluginType
            .GetCustomAttributes<InsiderPluginDependencyAttribute>(inherit: false)
            .OrderBy(attribute => attribute.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateDependency = dependencyAttributes
            .GroupBy(attribute => attribute.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDependency is not null)
        {
            results.Add(Fail(source, $"Plugin '{metadata.Id}' declares dependency '{duplicateDependency.Key}' more than once."));
            return null;
        }

        var dependencies = new List<PluginDependencyDescriptor>();
        foreach (var dependencyAttribute in dependencyAttributes)
        {
            PluginVersion? minimumVersion = null;
            if (dependencyAttribute.MinimumVersion is not null)
            {
                if (!PluginVersion.TryParse(dependencyAttribute.MinimumVersion, out var parsedMinimumVersion))
                {
                    results.Add(Fail(
                        source,
                        $"Plugin '{metadata.Id}' declares invalid minimum version '{dependencyAttribute.MinimumVersion}' for '{dependencyAttribute.Id}'. " +
                        "Expected MAJOR.MINOR.PATCH using non-negative integers."));
                    return null;
                }

                minimumVersion = parsedMinimumVersion;
            }

            dependencies.Add(new PluginDependencyDescriptor(
                dependencyAttribute.Id,
                dependencyAttribute.MinimumVersion,
                minimumVersion,
                dependencyAttribute.Optional));
        }

        return new PluginCandidate(pluginType, metadata, pluginVersion, dependencies.AsReadOnly(), source);
    }

    private PluginLoadResult Activate(PluginCandidate candidate)
    {
        if (_plugins.ContainsKey(candidate.Metadata.Id))
        {
            return Fail(candidate.Source, $"A plugin with id '{candidate.Metadata.Id}' is already loaded.");
        }

        var unavailable = candidate.Dependencies
            .Where(dependency => !dependency.Optional)
            .Select(GetLoadedRequirementFailure)
            .Where(failure => failure is not null)
            .Cast<string>()
            .ToArray();
        if (unavailable.Length > 0)
        {
            return Fail(
                candidate.Source,
                $"Plugin '{candidate.Metadata.Id}' was not loaded because required plugin dependencies are unavailable: {string.Join(", ", unavailable)}.");
        }

        IInsiderPlugin? instance = null;
        PluginContext? pluginContext = null;
        try
        {
            instance = (IInsiderPlugin?)Activator.CreateInstance(candidate.Type);
            if (instance is null)
            {
                return Fail(candidate.Source, $"Plugin type '{candidate.Type.FullName}' could not be instantiated.");
            }

            var descriptor = new PluginDescriptor(
                candidate.Metadata.Id,
                candidate.Metadata.Name,
                candidate.Metadata.Version,
                candidate.Type.FullName ?? candidate.Type.Name,
                candidate.Dependencies);
            pluginContext = new PluginContext(_context, descriptor.Id);
            instance.Load(pluginContext);

            _plugins.Add(descriptor.Id, new LoadedPlugin(descriptor, instance, pluginContext));
            _loadOrder.Add(descriptor.Id);
            _context.Logger.Info($"Loaded plugin {descriptor.Id} {descriptor.Version}.");
            return PluginLoadResult.Success(descriptor, candidate.Source);
        }
        catch (Exception exception)
        {
            TryUnloadPartial(instance, candidate.Source);
            TryDisposeContext(pluginContext, candidate.Source);
            _context.Logger.Error($"Plugin '{candidate.Source}' failed during load.", exception);
            return PluginLoadResult.Failure(candidate.Source, exception.Message, exception);
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
            finally
            {
                TryDisposeContext(plugin.Context, id);
            }
        }

        _plugins.Clear();
        _loadOrder.Clear();
        _dependencyResolver?.Dispose();
        _dependencyResolver = null;
    }

    public void Dispose()
    {
        UnloadAll();
    }

    private PluginLoadResult Fail(string source, string error)
    {
        _context.Logger.Warn(error);
        return PluginLoadResult.Failure(source, error);
    }

    private Dictionary<string, PluginCandidate> SelectUniqueCandidates(
        IEnumerable<PluginCandidate> candidates,
        ICollection<PluginLoadResult> results)
    {
        var selected = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates
            .GroupBy(candidate => candidate.Metadata.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupedCandidates = group.OrderBy(candidate => candidate.Source, StringComparer.OrdinalIgnoreCase).ToArray();
            if (groupedCandidates.Length > 1)
            {
                foreach (var candidate in groupedCandidates)
                {
                    results.Add(Fail(candidate.Source, $"Multiple discovered plugins declare id '{group.Key}'."));
                }

                continue;
            }

            var single = groupedCandidates[0];
            if (_plugins.ContainsKey(single.Metadata.Id))
            {
                results.Add(Fail(single.Source, $"A plugin with id '{single.Metadata.Id}' is already loaded."));
                continue;
            }

            selected.Add(single.Metadata.Id, single);
        }

        return selected;
    }

    private void RemoveCandidatesWithMissingDependencies(
        IDictionary<string, PluginCandidate> candidates,
        ICollection<PluginLoadResult> results)
    {
        while (true)
        {
            var invalid = candidates.Values
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Missing = candidate.Dependencies
                        .Where(dependency => !dependency.Optional)
                        .Select(dependency => GetRequirementFailure(dependency, candidates))
                        .Where(failure => failure is not null)
                        .Cast<string>()
                        .ToArray(),
                })
                .Where(item => item.Missing.Length > 0)
                .OrderBy(item => item.Candidate.Metadata.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (invalid.Length == 0)
            {
                return;
            }

            foreach (var item in invalid)
            {
                candidates.Remove(item.Candidate.Metadata.Id);
                results.Add(Fail(
                    item.Candidate.Source,
                    $"Plugin '{item.Candidate.Metadata.Id}' has unsatisfied required plugin dependencies: {string.Join(", ", item.Missing)}."));
            }
        }
    }

    private IReadOnlyList<PluginCandidate> CreateLoadOrder(
        IDictionary<string, PluginCandidate> candidates,
        ICollection<PluginLoadResult> results)
    {
        var remaining = new Dictionary<string, PluginCandidate>(candidates, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PluginCandidate>();

        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(candidate => candidate.Dependencies.All(
                    dependency => dependency.Optional || !remaining.ContainsKey(dependency.Id)))
                .OrderBy(candidate => candidate.Metadata.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ready.Length == 0)
            {
                foreach (var candidate in remaining.Values.OrderBy(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(Fail(
                        candidate.Source,
                        $"Plugin '{candidate.Metadata.Id}' has a required dependency cycle or depends on a cyclic plugin."));
                }

                break;
            }

            var preferred = ready.FirstOrDefault(candidate => candidate.Dependencies.All(
                dependency =>
                    !dependency.Optional ||
                    !remaining.TryGetValue(dependency.Id, out var optionalCandidate) ||
                    !SatisfiesMinimum(optionalCandidate.Version, dependency)));
            var next = preferred ?? ready[0];
            remaining.Remove(next.Metadata.Id);
            ordered.Add(next);
        }

        return ordered;
    }

    private string? GetRequirementFailure(
        PluginDependencyDescriptor dependency,
        IDictionary<string, PluginCandidate> candidates)
    {
        if (_plugins.TryGetValue(dependency.Id, out var loaded))
        {
            return GetVersionFailure(dependency, loaded.Descriptor.Version);
        }

        if (candidates.TryGetValue(dependency.Id, out var candidate))
        {
            return SatisfiesMinimum(candidate.Version, dependency)
                ? null
                : FormatVersionFailure(dependency, candidate.Metadata.Version);
        }

        return $"{dependency.Id} (missing)";
    }

    private string? GetLoadedRequirementFailure(PluginDependencyDescriptor dependency)
    {
        if (!_plugins.TryGetValue(dependency.Id, out var loaded))
        {
            return $"{dependency.Id} (not loaded)";
        }

        return GetVersionFailure(dependency, loaded.Descriptor.Version);
    }

    private static string? GetVersionFailure(PluginDependencyDescriptor dependency, string actualVersion)
    {
        if (!PluginVersion.TryParse(actualVersion, out var parsedActualVersion))
        {
            return $"{dependency.Id} (invalid loaded version '{actualVersion}')";
        }

        return SatisfiesMinimum(parsedActualVersion, dependency)
            ? null
            : FormatVersionFailure(dependency, actualVersion);
    }

    private static bool SatisfiesMinimum(PluginVersion actualVersion, PluginDependencyDescriptor dependency)
    {
        return dependency.ParsedMinimumVersion is null ||
            actualVersion.CompareTo(dependency.ParsedMinimumVersion.Value) >= 0;
    }

    private static string FormatVersionFailure(PluginDependencyDescriptor dependency, string actualVersion)
    {
        return $"{dependency.Id} >= {dependency.MinimumVersion} (found {actualVersion})";
    }

    private void DiscoverAssembly(
        string assemblyPath,
        ICollection<Type> pluginTypes,
        ICollection<PluginLoadResult> results)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(assemblyPath);
            var assembly = Assembly.Load(File.ReadAllBytes(normalizedPath));
            foreach (var pluginType in GetLoadableTypes(assembly).Where(IsPluginType))
            {
                pluginTypes.Add(pluginType);
            }
        }
        catch (Exception exception)
        {
            _context.Logger.Error($"Could not load plugin assembly '{assemblyPath}'.", exception);
            results.Add(PluginLoadResult.Failure(assemblyPath, exception.Message, exception));
        }
    }

    private void EnsureDependencyResolver(string pluginDirectory)
    {
        if (_dependencyResolver is null)
        {
            _dependencyResolver = PluginAssemblyResolver.Create(
                pluginDirectory,
                Path.Combine(_context.InsiderDirectory, "core"),
                _context.Logger);
            return;
        }

        if (!string.Equals(_dependencyResolver.PluginDirectory, pluginDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This plugin host is already bound to '{_dependencyResolver.PluginDirectory}' and cannot also load '{pluginDirectory}'.");
        }
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

    private void TryDisposeContext(PluginContext? context, string source)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            context.Dispose();
        }
        catch (Exception exception)
        {
            _context.Logger.Error($"Plugin '{source}' hook cleanup failed.", exception);
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
        public LoadedPlugin(PluginDescriptor descriptor, IInsiderPlugin instance, PluginContext context)
        {
            Descriptor = descriptor;
            Instance = instance;
            Context = context;
        }

        public PluginDescriptor Descriptor { get; }

        public IInsiderPlugin Instance { get; }

        public PluginContext Context { get; }
    }
}

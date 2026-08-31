using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Insider.Loader;

internal sealed class PluginAssemblyResolver : IDisposable
{
    private readonly IReadOnlyDictionary<string, AssemblyCandidate> _assembliesByIdentity;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<AssemblyCandidate>> _assembliesByName;
    private readonly IInsiderLogger _logger;
    private readonly object _sync = new object();
    private bool _disposed;

    private PluginAssemblyResolver(
        string pluginDirectory,
        IReadOnlyDictionary<string, AssemblyCandidate> assembliesByIdentity,
        IReadOnlyDictionary<string, IReadOnlyList<AssemblyCandidate>> assembliesByName,
        IInsiderLogger logger)
    {
        PluginDirectory = pluginDirectory;
        _assembliesByIdentity = assembliesByIdentity;
        _assembliesByName = assembliesByName;
        _logger = logger;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    public string PluginDirectory { get; }

    public static PluginAssemblyResolver Create(string pluginDirectory, IInsiderLogger logger)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("A plugin directory is required.", nameof(pluginDirectory));
        }

        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        var normalizedDirectory = Path.GetFullPath(pluginDirectory);
        var candidates = DiscoverCandidates(normalizedDirectory);
        ValidateCandidateConflicts(candidates);
        ValidateLoadedAssemblyConflicts(candidates);

        var byIdentity = candidates.ToDictionary(
            candidate => candidate.Identity.FullName,
            candidate => candidate,
            StringComparer.OrdinalIgnoreCase);
        var byName = candidates
            .GroupBy(candidate => candidate.Identity.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AssemblyCandidate>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new PluginAssemblyResolver(normalizedDirectory, byIdentity, byName, logger);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            _disposed = true;
        }
    }

    private Assembly? Resolve(object? sender, ResolveEventArgs eventArgs)
    {
        AssemblyName requestedIdentity;
        try
        {
            requestedIdentity = new AssemblyName(eventArgs.Name);
        }
        catch (Exception exception)
        {
            _logger.Error($"Could not parse requested assembly identity '{eventArgs.Name}'.", exception);
            return null;
        }

        if (requestedIdentity.Name?.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            var requestedFullName = requestedIdentity.FullName;
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                assembly => string.Equals(assembly.GetName().FullName, requestedFullName, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
            {
                return loaded;
            }

            if (_assembliesByIdentity.TryGetValue(requestedFullName, out var candidate))
            {
                try
                {
                    var resolved = Assembly.Load(File.ReadAllBytes(candidate.Path));
                    _logger.Info($"Resolved plugin dependency '{requestedFullName}' from '{candidate.Path}'.");
                    return resolved;
                }
                catch (Exception exception)
                {
                    _logger.Error($"Could not load plugin dependency '{requestedFullName}' from '{candidate.Path}'.", exception);
                    return null;
                }
            }

            if (_assembliesByName.TryGetValue(requestedIdentity.Name ?? string.Empty, out var alternatives))
            {
                var available = string.Join(", ", alternatives.Select(item => $"'{item.Identity.FullName}'"));
                _logger.Error($"Plugin requested '{requestedFullName}', but only {available} is available under '{PluginDirectory}'.");
            }
            else
            {
                _logger.Error($"Plugin dependency '{requestedFullName}' is not present under '{PluginDirectory}'.");
            }

            return null;
        }
    }

    private static IReadOnlyList<AssemblyCandidate> DiscoverCandidates(string pluginDirectory)
    {
        var paths = new List<string>();
        paths.AddRange(Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly));

        var dependencyDirectory = Path.Combine(pluginDirectory, "dependencies");
        if (Directory.Exists(dependencyDirectory))
        {
            paths.AddRange(Directory.GetFiles(dependencyDirectory, "*.dll", SearchOption.AllDirectories));
        }

        var candidates = new List<AssemblyCandidate>();
        foreach (var path in paths.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var identity = AssemblyName.GetAssemblyName(path);
                if (string.IsNullOrWhiteSpace(identity.Name) || string.IsNullOrWhiteSpace(identity.FullName))
                {
                    continue;
                }

                candidates.Add(new AssemblyCandidate(Path.GetFullPath(path), identity));
            }
            catch (BadImageFormatException)
            {
                // Native libraries can live beside managed plugin dependencies.
            }
        }

        return candidates;
    }

    private static void ValidateCandidateConflicts(IReadOnlyList<AssemblyCandidate> candidates)
    {
        var conflict = candidates
            .GroupBy(candidate => candidate.Identity.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (conflict is null)
        {
            return;
        }

        var details = string.Join(
            "; ",
            conflict.Select(candidate => $"'{candidate.Identity.FullName}' at '{candidate.Path}'"));
        throw new PluginDependencyConflictException(
            $"Dependency catalog contains conflicting assemblies for '{conflict.Key}': {details}. " +
            "Insider cannot safely load multiple candidates with the same simple name in one Unity AppDomain.");
    }

    private static void ValidateLoadedAssemblyConflicts(IReadOnlyList<AssemblyCandidate> candidates)
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName())
            .ToArray();

        foreach (var candidate in candidates)
        {
            var conflict = loadedAssemblies.FirstOrDefault(
                loaded => string.Equals(loaded.Name, candidate.Identity.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(loaded.FullName, candidate.Identity.FullName, StringComparison.OrdinalIgnoreCase));
            if (conflict is null)
            {
                continue;
            }

            throw new PluginDependencyConflictException(
                $"Plugin assembly '{candidate.Identity.FullName}' at '{candidate.Path}' conflicts with the already loaded " +
                $"assembly '{conflict.FullName}'. Insider cannot isolate both identities in the current Unity AppDomain.");
        }
    }

    private sealed class AssemblyCandidate
    {
        public AssemblyCandidate(string path, AssemblyName identity)
        {
            Path = path;
            Identity = identity;
        }

        public string Path { get; }

        public AssemblyName Identity { get; }
    }
}

internal sealed class PluginDependencyConflictException : Exception
{
    public PluginDependencyConflictException(string message)
        : base(message)
    {
    }
}

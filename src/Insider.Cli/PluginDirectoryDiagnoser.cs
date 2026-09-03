using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Insider.Cli;

internal static class PluginDirectoryDiagnoser
{
    private static readonly DiagnosticVersion CurrentInsiderVersion = DiagnosticVersion.FromAssemblyVersion(
        typeof(InsiderPluginAttribute).Assembly.GetName().Version);

    public static PluginDirectoryDiagnosticReport Inspect(
        string pluginDirectory,
        string coreDirectory,
        IEnumerable<string> disabledPluginIds)
    {
        var disabled = new HashSet<string>(disabledPluginIds, StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(pluginDirectory))
        {
            return new PluginDirectoryDiagnosticReport(
                Array.Empty<PluginDiagnostic>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        var assemblyPaths = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidates = new List<PluginCandidateDiagnostic>();
        var problems = new List<string>();

        using (var context = new DiagnosticLoadContext(pluginDirectory, coreDirectory))
        {
            problems.AddRange(context.CatalogProblems);
            foreach (var assemblyPath in assemblyPaths)
            {
                DiscoverAssembly(context, assemblyPath, candidates, problems);
            }
        }

        MarkDuplicateIds(candidates);
        ResolvePluginStates(candidates, disabled);
        PopulateDependencyStatuses(candidates, disabled);

        foreach (var candidate in candidates)
        {
            problems.AddRange(candidate.Issues.Select(issue => $"Plugin '{candidate.Id}': {issue}"));
        }

        var knownIds = new HashSet<string>(
            candidates.Where(candidate => candidate.HasMetadata).Select(candidate => candidate.Id),
            StringComparer.OrdinalIgnoreCase);
        var notes = disabled
            .Where(pluginId => !knownIds.Contains(pluginId))
            .OrderBy(pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
            .Select(pluginId => $"Disabled ID '{pluginId}' does not match a discovered plugin.")
            .ToArray();

        var plugins = candidates
            .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.AssemblyPath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.ToResult())
            .ToArray();
        return new PluginDirectoryDiagnosticReport(
            plugins,
            problems.OrderBy(problem => problem, StringComparer.OrdinalIgnoreCase).ToArray(),
            notes);
    }

    private static void DiscoverAssembly(
        DiagnosticLoadContext context,
        string assemblyPath,
        ICollection<PluginCandidateDiagnostic> candidates,
        ICollection<string> problems)
    {
        try
        {
            var assembly = context.LoadPluginAssembly(assemblyPath);
            foreach (var type in GetLoadableTypes(assembly, assemblyPath, problems).Where(IsPluginType))
            {
                candidates.Add(CreateCandidate(type, assemblyPath));
            }
        }
        catch (Exception exception)
        {
            problems.Add($"Assembly '{assemblyPath}': {ReadableMessage(exception)}");
        }
    }

    private static PluginCandidateDiagnostic CreateCandidate(Type type, string assemblyPath)
    {
        var fallbackId = type.FullName ?? type.Name;
        try
        {
            var metadata = type.GetCustomAttribute<InsiderPluginAttribute>(inherit: false);
            if (metadata is null)
            {
                var missing = new PluginCandidateDiagnostic(
                    fallbackId,
                    type.Name,
                    "unknown",
                    null,
                    null,
                    assemblyPath,
                    hasMetadata: false,
                    Array.Empty<PluginDependencyCandidate>());
                missing.Issues.Add($"Type '{fallbackId}' is missing InsiderPluginAttribute.");
                return missing;
            }

            DiagnosticVersion? parsedVersion = null;
            var candidate = new PluginCandidateDiagnostic(
                metadata.Id,
                metadata.Name,
                metadata.Version,
                null,
                metadata.MinimumInsiderVersion,
                assemblyPath,
                hasMetadata: true,
                CreateDependencies(type));
            if (!DiagnosticVersion.TryParse(metadata.Version, out var version))
            {
                candidate.Issues.Add(
                    $"Version '{metadata.Version}' is invalid; expected MAJOR.MINOR.PATCH using non-negative integers.");
            }
            else
            {
                parsedVersion = version;
            }

            candidate.ParsedVersion = parsedVersion;
            if (metadata.MinimumInsiderVersion is not null)
            {
                if (!DiagnosticVersion.TryParse(metadata.MinimumInsiderVersion, out var minimumInsiderVersion))
                {
                    candidate.Issues.Add(
                        $"Minimum Insider version '{metadata.MinimumInsiderVersion}' is invalid; expected MAJOR.MINOR.PATCH.");
                }
                else if (CurrentInsiderVersion.CompareTo(minimumInsiderVersion) < 0)
                {
                    candidate.Issues.Add(
                        $"Requires Insider >= {minimumInsiderVersion}, but this Insider build is {CurrentInsiderVersion}.");
                }
            }

            var duplicateDependency = candidate.Dependencies
                .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateDependency is not null)
            {
                candidate.Issues.Add($"Dependency '{duplicateDependency.Key}' is declared more than once.");
            }

            foreach (var dependency in candidate.Dependencies)
            {
                if (dependency.MinimumVersion is not null && dependency.ParsedMinimumVersion is null)
                {
                    candidate.Issues.Add(
                        $"Minimum version '{dependency.MinimumVersion}' for '{dependency.Id}' is invalid; expected MAJOR.MINOR.PATCH.");
                }
            }

            return candidate;
        }
        catch (Exception exception)
        {
            var invalid = new PluginCandidateDiagnostic(
                fallbackId,
                type.Name,
                "unknown",
                null,
                null,
                assemblyPath,
                hasMetadata: false,
                Array.Empty<PluginDependencyCandidate>());
            invalid.Issues.Add($"Metadata could not be read: {ReadableMessage(exception)}");
            return invalid;
        }
    }

    private static IReadOnlyList<PluginDependencyCandidate> CreateDependencies(Type type)
    {
        return type.GetCustomAttributes<InsiderPluginDependencyAttribute>(inherit: false)
            .OrderBy(attribute => attribute.Id, StringComparer.OrdinalIgnoreCase)
            .Select(attribute =>
            {
                DiagnosticVersion? minimum = null;
                if (attribute.MinimumVersion is not null &&
                    DiagnosticVersion.TryParse(attribute.MinimumVersion, out var parsed))
                {
                    minimum = parsed;
                }

                return new PluginDependencyCandidate(
                    attribute.Id,
                    attribute.MinimumVersion,
                    minimum,
                    attribute.Optional);
            })
            .ToArray();
    }

    private static void MarkDuplicateIds(IEnumerable<PluginCandidateDiagnostic> candidates)
    {
        foreach (var group in candidates
            .Where(candidate => candidate.HasMetadata)
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var candidate in group)
            {
                candidate.Issues.Add($"Multiple discovered plugins declare ID '{group.Key}'.");
            }
        }
    }

    private static void ResolvePluginStates(
        IReadOnlyCollection<PluginCandidateDiagnostic> candidates,
        ISet<string> disabled)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Issues.Count > 0)
            {
                candidate.State = PluginDiagnosticState.Problem;
            }
            else if (candidate.HasMetadata && disabled.Contains(candidate.Id))
            {
                candidate.State = PluginDiagnosticState.Disabled;
            }
        }

        var available = candidates
            .Where(candidate => candidate.State == PluginDiagnosticState.Unknown)
            .ToDictionary(candidate => candidate.Id, candidate => candidate, StringComparer.OrdinalIgnoreCase);
        var known = candidates
            .Where(candidate => candidate.HasMetadata)
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var invalid = available.Values
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Failures = candidate.Dependencies
                        .Where(dependency => !dependency.Optional)
                        .Select(dependency => GetRequirementFailure(dependency, available, known, disabled))
                        .Where(failure => failure is not null)
                        .Cast<string>()
                        .ToArray(),
                })
                .Where(item => item.Failures.Length > 0)
                .ToArray();
            if (invalid.Length == 0)
            {
                break;
            }

            foreach (var item in invalid)
            {
                available.Remove(item.Candidate.Id);
                item.Candidate.State = PluginDiagnosticState.Problem;
                item.Candidate.Issues.Add(
                    $"Unsatisfied required dependencies: {string.Join(", ", item.Failures)}.");
            }
        }

        var remaining = new Dictionary<string, PluginCandidateDiagnostic>(available, StringComparer.OrdinalIgnoreCase);
        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(candidate => candidate.Dependencies.All(
                    dependency => dependency.Optional || !remaining.ContainsKey(dependency.Id)))
                .ToArray();
            if (ready.Length == 0)
            {
                foreach (var candidate in remaining.Values)
                {
                    candidate.State = PluginDiagnosticState.Problem;
                    candidate.Issues.Add("A required dependency cycle exists or this plugin depends on a cyclic plugin.");
                }

                break;
            }

            foreach (var candidate in ready)
            {
                remaining.Remove(candidate.Id);
                candidate.State = PluginDiagnosticState.Ready;
            }
        }
    }

    private static string? GetRequirementFailure(
        PluginDependencyCandidate dependency,
        IDictionary<string, PluginCandidateDiagnostic> available,
        IReadOnlyDictionary<string, PluginCandidateDiagnostic> known,
        ISet<string> disabled)
    {
        if (available.TryGetValue(dependency.Id, out var candidate))
        {
            return SatisfiesMinimum(candidate, dependency)
                ? null
                : $"{dependency.Id} >= {dependency.MinimumVersion} (found {candidate.Version})";
        }

        if (disabled.Contains(dependency.Id) ||
            (known.TryGetValue(dependency.Id, out var knownCandidate) && knownCandidate.State == PluginDiagnosticState.Disabled))
        {
            return $"{dependency.Id} (disabled)";
        }

        return known.ContainsKey(dependency.Id)
            ? $"{dependency.Id} (has problems)"
            : $"{dependency.Id} (missing)";
    }

    private static void PopulateDependencyStatuses(
        IReadOnlyCollection<PluginCandidateDiagnostic> candidates,
        ISet<string> disabled)
    {
        var known = candidates
            .Where(candidate => candidate.HasMetadata)
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            foreach (var dependency in candidate.Dependencies)
            {
                if (!known.TryGetValue(dependency.Id, out var target))
                {
                    dependency.Status = dependency.Optional ? "not installed (allowed)" : "missing";
                }
                else if (disabled.Contains(dependency.Id) || target.State == PluginDiagnosticState.Disabled)
                {
                    dependency.Status = dependency.Optional ? "disabled (allowed)" : "disabled";
                }
                else if (target.State == PluginDiagnosticState.Problem)
                {
                    dependency.Status = dependency.Optional ? "has problems (allowed)" : "has problems";
                }
                else if (!SatisfiesMinimum(target, dependency))
                {
                    dependency.Status = dependency.Optional
                        ? $"found {target.Version}, below the optional minimum (allowed)"
                        : $"found {target.Version}, below the minimum";
                }
                else
                {
                    dependency.Status = $"ready ({target.Version})";
                }
            }
        }
    }

    private static bool SatisfiesMinimum(
        PluginCandidateDiagnostic candidate,
        PluginDependencyCandidate dependency)
    {
        return dependency.ParsedMinimumVersion is null ||
            (candidate.ParsedVersion is not null &&
                candidate.ParsedVersion.Value.CompareTo(dependency.ParsedMinimumVersion.Value) >= 0);
    }

    private static IEnumerable<Type> GetLoadableTypes(
        Assembly assembly,
        string assemblyPath,
        ICollection<string> problems)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            foreach (var loaderException in exception.LoaderExceptions.Where(item => item is not null))
            {
                problems.Add($"Assembly '{assemblyPath}': {ReadableMessage(loaderException!)}");
            }

            return exception.Types.Where(type => type is not null).Cast<Type>();
        }
    }

    private static bool IsPluginType(Type type)
    {
        return typeof(IInsiderPlugin).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract;
    }

    private static string ReadableMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}

internal sealed class PluginDirectoryDiagnosticReport
{
    public PluginDirectoryDiagnosticReport(
        IReadOnlyList<PluginDiagnostic> plugins,
        IReadOnlyList<string> problems,
        IReadOnlyList<string> notes)
    {
        Plugins = plugins;
        Problems = problems;
        Notes = notes;
    }

    public IReadOnlyList<PluginDiagnostic> Plugins { get; }

    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyList<string> Notes { get; }
}

internal enum PluginDiagnosticState
{
    Unknown,
    Ready,
    Disabled,
    Problem,
}

internal sealed class PluginDiagnostic
{
    public PluginDiagnostic(
        string id,
        string name,
        string version,
        string? minimumInsiderVersion,
        string assemblyPath,
        PluginDiagnosticState state,
        IReadOnlyList<PluginDependencyDiagnostic> dependencies,
        IReadOnlyList<string> issues)
    {
        Id = id;
        Name = name;
        Version = version;
        MinimumInsiderVersion = minimumInsiderVersion;
        AssemblyPath = assemblyPath;
        State = state;
        Dependencies = dependencies;
        Issues = issues;
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string? MinimumInsiderVersion { get; }

    public string AssemblyPath { get; }

    public PluginDiagnosticState State { get; }

    public IReadOnlyList<PluginDependencyDiagnostic> Dependencies { get; }

    public IReadOnlyList<string> Issues { get; }
}

internal sealed class PluginDependencyDiagnostic
{
    public PluginDependencyDiagnostic(string id, string? minimumVersion, bool optional, string status)
    {
        Id = id;
        MinimumVersion = minimumVersion;
        Optional = optional;
        Status = status;
    }

    public string Id { get; }

    public string? MinimumVersion { get; }

    public bool Optional { get; }

    public string Status { get; }
}

internal sealed class PluginCandidateDiagnostic
{
    public PluginCandidateDiagnostic(
        string id,
        string name,
        string version,
        DiagnosticVersion? parsedVersion,
        string? minimumInsiderVersion,
        string assemblyPath,
        bool hasMetadata,
        IReadOnlyList<PluginDependencyCandidate> dependencies)
    {
        Id = id;
        Name = name;
        Version = version;
        ParsedVersion = parsedVersion;
        MinimumInsiderVersion = minimumInsiderVersion;
        AssemblyPath = assemblyPath;
        HasMetadata = hasMetadata;
        Dependencies = dependencies;
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    public DiagnosticVersion? ParsedVersion { get; set; }

    public string? MinimumInsiderVersion { get; }

    public string AssemblyPath { get; }

    public bool HasMetadata { get; }

    public IReadOnlyList<PluginDependencyCandidate> Dependencies { get; }

    public List<string> Issues { get; } = new List<string>();

    public PluginDiagnosticState State { get; set; }

    public PluginDiagnostic ToResult()
    {
        return new PluginDiagnostic(
            Id,
            Name,
            Version,
            MinimumInsiderVersion,
            AssemblyPath,
            State,
            Dependencies.Select(dependency => new PluginDependencyDiagnostic(
                dependency.Id,
                dependency.MinimumVersion,
                dependency.Optional,
                dependency.Status)).ToArray(),
            Issues.AsReadOnly());
    }
}

internal sealed class PluginDependencyCandidate
{
    public PluginDependencyCandidate(
        string id,
        string? minimumVersion,
        DiagnosticVersion? parsedMinimumVersion,
        bool optional)
    {
        Id = id;
        MinimumVersion = minimumVersion;
        ParsedMinimumVersion = parsedMinimumVersion;
        Optional = optional;
    }

    public string Id { get; }

    public string? MinimumVersion { get; }

    public DiagnosticVersion? ParsedMinimumVersion { get; }

    public bool Optional { get; }

    public string Status { get; set; } = "unknown";
}

internal readonly struct DiagnosticVersion : IComparable<DiagnosticVersion>
{
    private DiagnosticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public static bool TryParse(string? value, out DiagnosticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value!.Split('.');
        return parts.Length == 3 &&
            TryParsePart(parts[0], out var major) &&
            TryParsePart(parts[1], out var minor) &&
            TryParsePart(parts[2], out var patch) &&
            SetVersion(major, minor, patch, out version);
    }

    public static DiagnosticVersion FromAssemblyVersion(Version? version)
    {
        return version is null
            ? new DiagnosticVersion(0, 0, 0)
            : new DiagnosticVersion(
                Math.Max(version.Major, 0),
                Math.Max(version.Minor, 0),
                Math.Max(version.Build, 0));
    }

    public int CompareTo(DiagnosticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.{1}.{2}",
            Major,
            Minor,
            Patch);
    }

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        return part.Length > 0 &&
            (part.Length == 1 || part[0] != '0') &&
            int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool SetVersion(int major, int minor, int patch, out DiagnosticVersion version)
    {
        version = new DiagnosticVersion(major, minor, patch);
        return true;
    }
}

internal sealed class DiagnosticLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<AssemblyCandidate>> _candidatesByName;
    private readonly Dictionary<string, Assembly> _loadedByPath =
        new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

    public DiagnosticLoadContext(string pluginDirectory, string coreDirectory)
        : base("Insider.Cli.Diagnostics", isCollectible: true)
    {
        var paths = new List<string>();
        paths.AddRange(Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly));
        if (Directory.Exists(coreDirectory))
        {
            paths.AddRange(Directory.GetFiles(coreDirectory, "*.dll", SearchOption.TopDirectoryOnly));
        }

        var dependencyDirectory = Path.Combine(pluginDirectory, "dependencies");
        if (Directory.Exists(dependencyDirectory))
        {
            paths.AddRange(Directory.GetFiles(dependencyDirectory, "*.dll", SearchOption.AllDirectories));
        }

        var candidates = paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(TryCreateCandidate)
            .Where(candidate => candidate is not null)
            .Cast<AssemblyCandidate>()
            .ToArray();
        _candidatesByName = candidates
            .GroupBy(candidate => candidate.Identity.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AssemblyCandidate>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        CatalogProblems = _candidatesByName
            .Where(pair => pair.Value.Count > 1)
            .Select(pair =>
                $"Managed dependency '{pair.Key}' has multiple candidates: " +
                string.Join("; ", pair.Value.Select(candidate => $"'{candidate.Identity.FullName}' at '{candidate.Path}'")) + ".")
            .ToArray();
    }

    public IReadOnlyList<string> CatalogProblems { get; }

    public Assembly LoadPluginAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_loadedByPath.TryGetValue(fullPath, out var loaded))
        {
            return loaded;
        }

        using var stream = File.OpenRead(fullPath);
        var assembly = LoadFromStream(stream);
        _loadedByPath.Add(fullPath, assembly);
        return assembly;
    }

    public void Dispose()
    {
        Unload();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var abstractions = typeof(IInsiderPlugin).Assembly;
        if (string.Equals(
            assemblyName.Name,
            abstractions.GetName().Name,
            StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                assemblyName.FullName,
                abstractions.GetName().FullName,
                StringComparison.OrdinalIgnoreCase)
                ? abstractions
                : null;
        }

        if (assemblyName.Name is null ||
            !_candidatesByName.TryGetValue(assemblyName.Name, out var candidates) ||
            candidates.Count != 1)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!string.Equals(candidate.Identity.FullName, assemblyName.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return LoadPluginAssembly(candidate.Path);
    }

    private static AssemblyCandidate? TryCreateCandidate(string path)
    {
        try
        {
            var identity = AssemblyName.GetAssemblyName(path);
            return string.IsNullOrWhiteSpace(identity.Name) ? null : new AssemblyCandidate(Path.GetFullPath(path), identity);
        }
        catch (BadImageFormatException)
        {
            return null;
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

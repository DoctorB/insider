using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Insider.Installation;

public sealed class InsiderInstaller
{
    private const int CurrentSchemaVersion = 1;
    private const string ManifestRelativePath = "Insider/install.json";
    private const string BackupRelativePath = "Insider/backup/version.dll";

    private static readonly BundleFile[] RequiredFiles =
    {
        new BundleFile("native/win-x64/version.dll", "version.dll", "native-bootstrap"),
        new BundleFile("core/Insider.Abstractions.dll", "Insider/core/Insider.Abstractions.dll", "managed-core"),
        new BundleFile("core/Insider.Loader.dll", "Insider/core/Insider.Loader.dll", "managed-core"),
        new BundleFile("core/Insider.Bootstrap.dll", "Insider/core/Insider.Bootstrap.dll", "managed-core"),
        new BundleFile("core/Insider.Hooking.dll", "Insider/core/Insider.Hooking.dll", "hooking-backend"),
        new BundleFile("core/Mono.Cecil.dll", "Insider/core/Mono.Cecil.dll", "hooking-runtime"),
        new BundleFile("core/Mono.Cecil.Mdb.dll", "Insider/core/Mono.Cecil.Mdb.dll", "hooking-runtime"),
        new BundleFile("core/Mono.Cecil.Pdb.dll", "Insider/core/Mono.Cecil.Pdb.dll", "hooking-runtime"),
        new BundleFile("core/Mono.Cecil.Rocks.dll", "Insider/core/Mono.Cecil.Rocks.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.Backports.dll", "Insider/core/MonoMod.Backports.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.Core.dll", "Insider/core/MonoMod.Core.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.Iced.dll", "Insider/core/MonoMod.Iced.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.ILHelpers.dll", "Insider/core/MonoMod.ILHelpers.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.RuntimeDetour.dll", "Insider/core/MonoMod.RuntimeDetour.dll", "hooking-runtime"),
        new BundleFile("core/MonoMod.Utils.dll", "Insider/core/MonoMod.Utils.dll", "hooking-runtime"),
        new BundleFile("core/System.Reflection.Emit.ILGeneration.dll", "Insider/core/System.Reflection.Emit.ILGeneration.dll", "hooking-runtime"),
        new BundleFile("core/System.Reflection.Emit.Lightweight.dll", "Insider/core/System.Reflection.Emit.Lightweight.dll", "hooking-runtime"),
    };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public InsiderInstallationStatus Install(string gameExecutable, string bundleDirectory)
    {
        var gamePath = RequireFile(gameExecutable, "Game executable");
        var gameDirectory = Path.GetDirectoryName(gamePath)
            ?? throw new InsiderInstallationException("The game executable has no parent directory.");
        var bundlePath = RequireDirectory(bundleDirectory, "Bundle directory");
        var manifestPath = ResolveWithin(gameDirectory, ManifestRelativePath);
        var backupPath = ResolveWithin(gameDirectory, BackupRelativePath);

        if (File.Exists(manifestPath))
        {
            throw new InsiderInstallationException(
                "Insider is already installed. Uninstall it before installing another bundle.");
        }

        if (File.Exists(backupPath))
        {
            throw new InsiderInstallationException(
                $"A previous version.dll backup already exists at '{backupPath}'. Move or restore it before installing.");
        }

        var files = RequiredFiles.Select(file => PrepareFile(bundlePath, gameDirectory, file)).ToArray();
        foreach (var file in files.Where(file => !IsRootProxy(file.TargetRelativePath)))
        {
            if (File.Exists(file.TargetPath))
            {
                throw new InsiderInstallationException(
                    $"Refusing to overwrite the unmanaged file '{file.TargetPath}'.");
            }
        }

        var installedPaths = new List<string>();
        var proxyPath = ResolveWithin(gameDirectory, "version.dll");
        var backupCreated = false;

        try
        {
            Directory.CreateDirectory(ResolveWithin(gameDirectory, "Insider/core"));
            Directory.CreateDirectory(ResolveWithin(gameDirectory, "Insider/plugins"));
            Directory.CreateDirectory(ResolveWithin(gameDirectory, "Insider/logs"));

            if (File.Exists(proxyPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Move(proxyPath, backupPath);
                backupCreated = true;
            }

            foreach (var file in files)
            {
                CopyAtomically(file.SourcePath, file.TargetPath);
                installedPaths.Add(file.TargetPath);
            }

            var manifest = new InstallationManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                InstalledAtUtc = DateTimeOffset.UtcNow,
                GameExecutable = Path.GetFileName(gamePath),
                HasVersionBackup = backupCreated,
                Files = files.Select(file => new InstalledFileRecord
                {
                    Path = NormalizeRelativePath(file.TargetRelativePath),
                    Role = file.Role,
                    Sha256 = file.Sha256,
                }).ToList(),
            };

            WriteManifestAtomically(manifestPath, manifest);
            return GetStatus(gamePath);
        }
        catch (Exception exception) when (exception is not InsiderInstallationException)
        {
            RollBackInstall(installedPaths, proxyPath, backupPath, backupCreated);
            throw new InsiderInstallationException("Insider installation failed and was rolled back.", exception);
        }
        catch
        {
            RollBackInstall(installedPaths, proxyPath, backupPath, backupCreated);
            throw;
        }
    }

    public InsiderInstallationStatus GetStatus(string gameExecutable)
    {
        var gamePath = Path.GetFullPath(gameExecutable);
        var gameDirectory = Path.GetDirectoryName(gamePath)
            ?? throw new InsiderInstallationException("The game executable has no parent directory.");
        var manifestPath = ResolveWithin(gameDirectory, ManifestRelativePath);

        if (!File.Exists(manifestPath))
        {
            return new InsiderInstallationStatus(
                InsiderInstallationState.NotInstalled,
                gameDirectory,
                Array.Empty<string>());
        }

        try
        {
            var manifest = ReadManifest(manifestPath);
            var issues = ValidateManifest(gameDirectory, manifest);
            return new InsiderInstallationStatus(
                issues.Count == 0 ? InsiderInstallationState.Installed : InsiderInstallationState.Damaged,
                gameDirectory,
                issues);
        }
        catch (Exception exception)
        {
            return new InsiderInstallationStatus(
                InsiderInstallationState.Damaged,
                gameDirectory,
                new[] { $"The installation manifest is invalid: {exception.Message}" });
        }
    }

    public InsiderInstallationStatus Uninstall(string gameExecutable, bool force = false)
    {
        var gamePath = Path.GetFullPath(gameExecutable);
        var gameDirectory = Path.GetDirectoryName(gamePath)
            ?? throw new InsiderInstallationException("The game executable has no parent directory.");
        var manifestPath = ResolveWithin(gameDirectory, ManifestRelativePath);

        if (!File.Exists(manifestPath))
        {
            return new InsiderInstallationStatus(
                InsiderInstallationState.NotInstalled,
                gameDirectory,
                Array.Empty<string>());
        }

        var manifest = ReadManifest(manifestPath);
        var issues = ValidateManifest(gameDirectory, manifest);
        if (issues.Count > 0 && !force)
        {
            throw new InsiderInstallationException(
                "The installation is damaged or modified. No files were removed. " +
                "Review `insider status` and use --force only if the changes can be discarded.");
        }

        foreach (var file in manifest.Files)
        {
            var targetPath = ResolveWithin(gameDirectory, file.Path);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }

        var backupPath = ResolveWithin(gameDirectory, BackupRelativePath);
        var proxyPath = ResolveWithin(gameDirectory, "version.dll");
        if (manifest.HasVersionBackup && File.Exists(backupPath))
        {
            File.Move(backupPath, proxyPath);
        }

        File.Delete(manifestPath);
        TryDeleteEmptyDirectory(Path.GetDirectoryName(backupPath)!);
        TryDeleteEmptyDirectory(ResolveWithin(gameDirectory, "Insider/core"));

        return new InsiderInstallationStatus(
            InsiderInstallationState.NotInstalled,
            gameDirectory,
            issues);
    }

    private static PreparedFile PrepareFile(string bundleDirectory, string gameDirectory, BundleFile file)
    {
        var sourcePath = ResolveWithin(bundleDirectory, file.SourceRelativePath);
        if (!File.Exists(sourcePath))
        {
            throw new InsiderInstallationException(
                $"The bundle is incomplete: '{file.SourceRelativePath}' is missing.");
        }

        return new PreparedFile(
            sourcePath,
            ResolveWithin(gameDirectory, file.TargetRelativePath),
            file.TargetRelativePath,
            file.Role,
            ComputeSha256(sourcePath));
    }

    private static List<string> ValidateManifest(string gameDirectory, InstallationManifest manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            return new List<string>
            {
                $"Unsupported manifest schema {manifest.SchemaVersion}; expected {CurrentSchemaVersion}.",
            };
        }

        var issues = new List<string>();
        foreach (var file in manifest.Files)
        {
            var path = ResolveWithin(gameDirectory, file.Path);
            if (!File.Exists(path))
            {
                issues.Add($"Missing installed file: {file.Path}");
                continue;
            }

            if (!string.Equals(ComputeSha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Modified installed file: {file.Path}");
            }
        }

        var backupPath = ResolveWithin(gameDirectory, BackupRelativePath);
        if (manifest.HasVersionBackup && !File.Exists(backupPath))
        {
            issues.Add($"Missing original proxy backup: {BackupRelativePath}");
        }
        else if (!manifest.HasVersionBackup && File.Exists(backupPath))
        {
            issues.Add($"Unexpected proxy backup: {BackupRelativePath}");
        }

        return issues;
    }

    private static InstallationManifest ReadManifest(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<InstallationManifest>(stream, JsonOptions);
        if (manifest is null || manifest.Files.Count == 0)
        {
            throw new InsiderInstallationException("The installation manifest is empty.");
        }

        return manifest;
    }

    private static void WriteManifestAtomically(string manifestPath, InstallationManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var temporaryPath = manifestPath + ".new";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, manifest, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, manifestPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void CopyAtomically(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temporaryPath = targetPath + ".insider-new";
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, targetPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RollBackInstall(
        IEnumerable<string> installedPaths,
        string proxyPath,
        string backupPath,
        bool backupCreated)
    {
        foreach (var path in installedPaths.Reverse())
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Preserve the original installation exception.
            }
        }

        try
        {
            if (backupCreated && File.Exists(backupPath) && !File.Exists(proxyPath))
            {
                File.Move(backupPath, proxyPath);
            }
        }
        catch
        {
            // Preserve the original installation exception.
        }
    }

    private static string RequireFile(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InsiderInstallationException($"{label} not found: '{fullPath}'.");
        }

        return fullPath;
    }

    private static string RequireDirectory(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new InsiderInstallationException($"{label} not found: '{fullPath}'.");
        }

        return fullPath;
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InsiderInstallationException($"Expected a relative path, received '{relativePath}'.");
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        var rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new InsiderInstallationException($"Path '{relativePath}' escapes its expected root.");
        }

        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using var algorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static bool IsRootProxy(string relativePath)
    {
        return string.Equals(NormalizeRelativePath(relativePath), "version.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
            // Empty directory cleanup is best effort and never removes user content.
        }
    }

    private sealed class BundleFile
    {
        public BundleFile(string sourceRelativePath, string targetRelativePath, string role)
        {
            SourceRelativePath = sourceRelativePath;
            TargetRelativePath = targetRelativePath;
            Role = role;
        }

        public string SourceRelativePath { get; }

        public string TargetRelativePath { get; }

        public string Role { get; }
    }

    private sealed class PreparedFile
    {
        public PreparedFile(
            string sourcePath,
            string targetPath,
            string targetRelativePath,
            string role,
            string sha256)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            TargetRelativePath = targetRelativePath;
            Role = role;
            Sha256 = sha256;
        }

        public string SourcePath { get; }

        public string TargetPath { get; }

        public string TargetRelativePath { get; }

        public string Role { get; }

        public string Sha256 { get; }
    }

    private sealed class InstallationManifest
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset InstalledAtUtc { get; set; }

        public string GameExecutable { get; set; } = string.Empty;

        public bool HasVersionBackup { get; set; }

        public List<InstalledFileRecord> Files { get; set; } = new List<InstalledFileRecord>();
    }

    private sealed class InstalledFileRecord
    {
        public string Path { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;
    }
}

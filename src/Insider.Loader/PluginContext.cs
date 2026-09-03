using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Insider.Loader;

internal sealed class PluginContext : IInsiderContext, IDisposable
{
    private static readonly ISet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    };

    private readonly IInsiderContext _inner;
    private readonly PluginHookService _hooks;
    private readonly PluginMainThread _mainThread;

    public PluginContext(IInsiderContext inner, string pluginId, string pluginDirectory)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("A plugin id is required.", nameof(pluginId));
        }

        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("A plugin directory is required.", nameof(pluginDirectory));
        }

        PluginDirectory = Path.GetFullPath(pluginDirectory);
        var pathSegment = GetPathSegment(pluginId.Trim());
        ConfigDirectory = Path.Combine(Path.GetFullPath(inner.ConfigDirectory), pathSegment);
        DataDirectory = Path.Combine(Path.GetFullPath(inner.DataDirectory), pathSegment);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);

        Logger = new PluginLogger(inner.Logger, pluginId);
        _mainThread = new PluginMainThread(inner.MainThread, Logger);
        _hooks = new PluginHookService(inner.Hooks);
    }

    public string GameDirectory => _inner.GameDirectory;

    public string InsiderDirectory => _inner.InsiderDirectory;

    public string PluginDirectory { get; }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public IInsiderLogger Logger { get; }

    public IInsiderRuntimeInfo Runtime => _inner.Runtime;

    public IInsiderMainThread MainThread => _mainThread;

    public IInsiderHookService Hooks => _hooks;

    public void Dispose()
    {
        _mainThread.Dispose();
        _hooks.Dispose();
    }

    private static string GetPathSegment(string pluginId)
    {
        if (IsPortablePathSegment(pluginId))
        {
            return pluginId.ToLowerInvariant();
        }

        using var algorithm = SHA256.Create();
        var normalizedId = pluginId.ToUpperInvariant();
        var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(normalizedId));
        return "plugin-" + BitConverter.ToString(hash)
            .Replace("-", string.Empty)
            .ToLower(CultureInfo.InvariantCulture);
    }

    private static bool IsPortablePathSegment(string pluginId)
    {
        if (pluginId.Length == 0 || pluginId.Length > 100 ||
            !IsAsciiLetterOrDigit(pluginId[0]) ||
            !IsAsciiLetterOrDigit(pluginId[pluginId.Length - 1]))
        {
            return false;
        }

        foreach (var character in pluginId)
        {
            if (!IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        var deviceNameEnd = pluginId.IndexOf('.');
        var deviceName = deviceNameEnd < 0 ? pluginId : pluginId.Substring(0, deviceNameEnd);
        return !ReservedDeviceNames.Contains(deviceName);
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    }
}

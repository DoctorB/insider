using System;
using System.Globalization;
using System.IO;

namespace Insider.Bootstrap;

internal sealed class FileLogger : IInsiderLogger
{
    private readonly object _sync = new object();
    private readonly string _path;

    public FileLogger(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

        var rotationFailure = TryRotateCurrentLog(_path);
        if (rotationFailure is not null)
        {
            Log(InsiderLogLevel.Warning, rotationFailure);
        }
    }

    public void Log(InsiderLogLevel level, string message, Exception? exception = null)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:O} [{1}] {2}{3}",
            DateTime.UtcNow,
            level,
            message,
            exception is null ? string.Empty : Environment.NewLine + exception);

        lock (_sync)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                try
                {
                    Console.Error.WriteLine(line);
                }
                catch
                {
                    // Logging is best effort inside the game process.
                }
            }
        }
    }

    private static string? TryRotateCurrentLog(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var previousPath = Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(path) + ".previous" + Path.GetExtension(path));

        try
        {
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }

            File.Move(path, previousPath);
            return null;
        }
        catch (Exception exception)
        {
            return $"Could not rotate '{path}' to '{previousPath}': {exception.Message}";
        }
    }
}

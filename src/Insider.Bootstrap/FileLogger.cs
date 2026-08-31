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
        _path = path ?? throw new ArgumentNullException(nameof(path));
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
}

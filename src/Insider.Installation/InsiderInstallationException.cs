using System;

namespace Insider.Installation;

public sealed class InsiderInstallationException : Exception
{
    public InsiderInstallationException(string message)
        : base(message)
    {
    }

    public InsiderInstallationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

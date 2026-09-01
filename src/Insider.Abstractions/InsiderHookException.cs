using System;

namespace Insider;

/// <summary>
/// Represents a failure while applying or removing a managed detour.
/// </summary>
public sealed class InsiderHookException : Exception
{
    public InsiderHookException(string message)
        : base(message)
    {
    }

    public InsiderHookException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

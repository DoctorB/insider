using System.Collections.Generic;

namespace Insider.Installation;

public enum InsiderInstallationState
{
    NotInstalled,
    Installed,
    Damaged,
}

public sealed class InsiderInstallationStatus
{
    internal InsiderInstallationStatus(
        InsiderInstallationState state,
        string gameDirectory,
        IReadOnlyList<string> issues)
    {
        State = state;
        GameDirectory = gameDirectory;
        Issues = issues;
    }

    public InsiderInstallationState State { get; }

    public string GameDirectory { get; }

    public IReadOnlyList<string> Issues { get; }
}

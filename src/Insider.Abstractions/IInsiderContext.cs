namespace Insider;

public interface IInsiderContext
{
    string GameDirectory { get; }

    string InsiderDirectory { get; }

    IInsiderLogger Logger { get; }

    IInsiderRuntimeInfo Runtime { get; }

    IInsiderHookService Hooks { get; }
}

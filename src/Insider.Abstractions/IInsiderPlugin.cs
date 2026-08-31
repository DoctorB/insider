namespace Insider;

public interface IInsiderPlugin
{
    void Load(IInsiderContext context);

    void Unload();
}

namespace Insider.Il2CppHost;

public static class EntryPoint
{
    public static int Main()
    {
        Insider.Native.Entrypoint.Start();
        return 0;
    }
}

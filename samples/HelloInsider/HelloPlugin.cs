using Insider;

namespace HelloInsider;

[InsiderPlugin("dev.insider.hello", "Hello Insider", "0.1.0")]
public sealed class HelloPlugin : IInsiderPlugin
{
    private IInsiderLogger? _logger;

    public void Load(IInsiderContext context)
    {
        _logger = context.Logger;
        _logger.Info($"Hello from {context.Runtime.Backend} on {context.Runtime.Architecture}.");
    }

    public void Unload()
    {
        _logger?.Info("Goodbye from Hello Insider.");
    }
}

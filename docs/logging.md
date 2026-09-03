# Logging

Insider keeps logging deliberately small and file based. There is no logging
configuration, retention policy, serializer, or external logging dependency.

## Files

Each game installation writes four possible files under `Insider/logs`:

| Layer | Current session | Previous session |
| --- | --- | --- |
| Native bootstrap | `native.log` | `native.previous.log` |
| Managed loader and plugins | `insider.log` | `insider.previous.log` |

When a game starts, an existing current log is moved to the matching
`*.previous.log` file before the first new message is written. An older previous
log is replaced, so each layer retains at most two files and disk usage cannot
grow through numbered log archives.

The native and managed files remain separate because the native bootstrap must
be able to report failures that happen before the managed loader starts.

## Plugin messages

Plugins should write through `context.Logger`:

```csharp
public void Load(IInsiderContext context)
{
    context.Logger.Info("Plugin loaded.");
    context.Logger.Warn("A recoverable condition occurred.");
}
```

Insider prefixes these messages with the plugin ID and writes them to the
current `insider.log`. Plugins should not write directly to Insider's own log
files because they are shared and rotated by the loader.

## Failure behavior

Logging and rotation are best effort. A file-system failure must not prevent the
game from starting. The managed logger reports a rotation failure to its normal
fallback output when possible; the native bootstrap simply continues writing if
Windows cannot move the file.

Changing filenames, retention counts, formats, or destinations is intentionally
outside the current scope.

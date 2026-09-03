# ADR 0011: Expose plugin-scoped directories

## Context

Plugins need predictable locations for bundled assets, user-editable settings,
and persistent runtime data. Rebuilding paths from the game directory or plugin
ID duplicates loader knowledge and risks collisions or directory traversal.

A general configuration framework would also impose formats, serialization,
and migration policy that are unrelated to the loader's core responsibility.

## Decision

Each `IInsiderContext` exposes three full paths:

- `PluginDirectory` is the directory containing the plugin entry assembly.
- `ConfigDirectory` is a persistent directory owned by that plugin's settings.
- `DataDirectory` is a persistent directory owned by that plugin's other data.

The loader creates the configuration and data directories before `Load()` and
isolates them by case-insensitive stable plugin ID. IDs that are not safe
portable path segments receive a deterministic hashed segment. Plugins must use
the paths supplied by the context rather than recreate the mapping.

`PluginDirectory` follows the entry assembly and can therefore be shared by
multiple plugin types in one assembly. It is not a persistent state directory.
Configuration and data remain in their assigned directories through unload and
Insider uninstall.

Insider provides no schema, serializer, cache abstraction, migration service,
or automatic cleanup.

## Consequences

- Plugins have simple, collision-free locations without depending on bootstrap
  layout details.
- A plugin owns cleanup and compatibility of files it writes below its
  configuration and data directories.
- Removing or upgrading loader files does not destroy plugin state.
- Plugin authors remain free to choose plain text, JSON, binary data, or no
  persistent format at all.
- The public contract grows by three read-only strings and no new framework.

# ADR 0004: Resolve plugin dependencies from one deterministic catalog

- Status: Accepted
- Date: 2026-08-31

## Context

Unity Mono loads Insider and all plugins into the game's existing application
domain. Loading dependencies opportunistically with `Assembly.LoadFrom` makes
results depend on file order, the game probing path, and assemblies the game has
already loaded. Creating a second runtime or claiming per-plugin isolation would
conflict with the loader's compatibility and ownership goals.

## Decision

Insider builds one dependency catalog for `Insider/plugins` and its
`dependencies` subtree before scanning plugin entry assemblies. Resolution uses
the requested full assembly identity. Duplicate simple names, different
versions with the same simple name, and conflicts with already loaded assemblies
fail closed with diagnostics.

An already loaded assembly with the same full identity may be reused by the
runtime. Insider rejects a different resident identity but does not attempt to
replace or isolate an identical one.

The resolver remains registered for the lifetime of the plugin host so lazy
managed dependencies can be resolved during plugin callbacks. It is detached
after reverse-order plugin unload. Managed images are loaded from bytes to avoid
holding source DLL files open, while the assemblies themselves remain loaded
until the process exits.

## Consequences

- Plugin startup is deterministic and conflicts are visible before plugin code
  runs.
- Plugin authors must share one dependency version per simple assembly name.
- Per-plugin side-by-side dependency isolation is not supported in the initial
  Unity Mono backend.
- A future backend may replace the catalog when the host runtime provides a
  proven isolation mechanism without changing the public plugin contract.

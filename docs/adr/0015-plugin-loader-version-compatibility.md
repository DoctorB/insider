# ADR 0015: Plugins may require one minimum Insider version

## Status

Accepted

## Context

A plugin can depend on loader APIs or behavior introduced after its first
release. Without an explicit requirement, an older Insider build may create the
plugin and enter `Load()` before discovering that the expected capability is
missing.

A general version-range language would add parsing and policy that the current
loader does not need.

## Decision

`InsiderPluginAttribute` exposes the optional named property
`MinimumInsiderVersion`. Both the plugin requirement and Insider's assembly
version use the existing strict `MAJOR.MINOR.PATCH` model.

The loader validates the requirement during metadata discovery. An invalid
requirement or a minimum greater than the current loader version produces a
failed load result before the plugin is instantiated or `Load()` is called. A
missing requirement keeps the existing behavior.

The abstractions and loader projects receive their version from one shared
`InsiderVersion` build property. The read-only CLI diagnostic applies the same
comparison without activating plugin code.

## Consequences

- Plugins can fail early with an actionable installed-versus-required message.
- Existing plugins remain compatible without source changes.
- The supported constraint is inclusive minimum only; maximum versions,
  arbitrary ranges, prerelease labels, and wildcards remain outside scope.
- A future Insider release must update the shared version property when its
  public compatibility level changes.

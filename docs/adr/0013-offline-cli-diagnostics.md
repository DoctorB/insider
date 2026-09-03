# ADR 0013: Keep CLI diagnostics offline and read-only

- Status: Accepted
- Date: 2026-09-03

## Context

Users need one way to understand whether an installed game, loader, and plugin
set are structurally coherent before launching Unity. Starting the game for a
health check makes failures slower and mixes configuration problems with runtime
behavior. Reusing normal plugin activation would also execute untrusted plugin
code during diagnosis.

A general telemetry system, configuration framework, or background service is
outside this requirement.

## Decision

The CLI adds:

```text
insider diagnose <game.exe>
```

It composes existing Unity inspection, installation-manifest verification, and
the disabled-plugin list with a temporary isolated assembly metadata scan. The
scan discovers `IInsiderPlugin` types and their Insider attributes but never
creates instances or calls lifecycle methods. It evaluates the loader's simple
plugin-ID dependency rules, including minimum versions, disabled requirements,
duplicates, and required cycles.

The command writes one human-readable report, returns `0` when it finds no
structural problems and `1` otherwise, and never modifies the game directory.

## Consequences

- Common setup and dependency failures are visible without starting Unity.
- Plugin code cannot run as a side effect of the diagnostic command.
- Disabled plugins remain visible in the catalog and stale disabled IDs are
  non-fatal notes.
- The scanner deliberately understands only Insider's current small metadata
  and dependency model; it is not a general .NET dependency analyzer.
- A clean report does not establish runtime or game compatibility.

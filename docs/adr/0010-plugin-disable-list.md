# ADR 0010: Disable plugins by stable ID

- Status: Accepted
- Date: 2026-09-02

## Context

A user needs to stop a problematic plugin from running without deleting its DLL
or changing the plugin dependency graph. A general configuration system, UI, hot
reload mechanism, or filename convention would add unrelated policy and moving
parts to the loader.

## Decision

The bootstrap reads the optional
`Insider/config/disabled-plugins.txt` file once per process start. Each active
line contains one plugin ID. Blank lines and lines beginning with `#` are
ignored; values are trimmed, de-duplicated, sorted, and compared without case
sensitivity.

The loader still discovers metadata and validates duplicate IDs before removing
disabled candidates. It removes them before dependency validation and activation,
does not create plugin instances, and does not report the intentional skip as a
failure. A required dependency on a disabled ID fails with a `(disabled)`
diagnostic; an optional dependency does not block activation.

The installer creates `Insider/config` but does not own files written there.
Uninstall therefore preserves the directory and its contents.

## Consequences

- A plugin can be disabled predictably without moving managed files.
- Changes take effect only after restarting the game.
- Assembly discovery still occurs, so disabling cannot hide an unreadable or
  structurally invalid assembly.
- There are no wildcards, filename rules, hot reload, UI, or new serialization
  dependency.

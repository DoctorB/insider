# ADR 0002: Keep the native bootstrap replaceable

- Status: Accepted
- Date: 2026-08-31

## Context

Loading managed code early in a Unity process requires platform-specific native
work. Reimplementing that mechanism before proving the managed loader would add
risk without validating the product experience.

## Decision

The managed bootstrap exposes a Doorstop-compatible entry point, while all
loader behavior remains in Insider-owned assemblies. UnityDoorstop is the first
planned bootstrap integration, distributed as a separate licensed component.

## Consequences

- Insider can replace the native bootstrap without changing plugins.
- Doorstop types and configuration do not appear in the public plugin contract.
- No native binary is committed until version, source, checksum, and license
  handling are automated and documented.

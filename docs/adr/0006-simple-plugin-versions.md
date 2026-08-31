# ADR 0006: Keep plugin version constraints deliberately simple

- Status: Accepted
- Date: 2026-08-31

## Context

Plugin dependency graphs need enough version information to reject an obsolete
provider. A full range language adds parsers, precedence corner cases, and
resolution behavior that the initial loader does not need.

## Decision

Plugin versions contain exactly three non-negative integers in
`MAJOR.MINOR.PATCH` form. Leading zeroes and suffixes are rejected. A dependency
may declare either no version constraint or one inclusive minimum version.

There are no comparison operators, compound ranges, wildcards, prerelease
labels, or build metadata. A required provider below the minimum blocks the
dependant. An optional provider below the minimum is treated as absent.

## Consequences

- Version comparison is a small lexicographic integer comparison.
- Invalid metadata fails before plugin activation with a direct diagnostic.
- The public contract remains easy to explain and implement across Unity Mono
  profiles.
- More expressive ranges require a new decision backed by a concrete use case.

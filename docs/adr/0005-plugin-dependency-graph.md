# ADR 0005: Plan plugin activation as a dependency graph

- Status: Accepted
- Date: 2026-08-31

## Context

Loading plugins immediately as assemblies are enumerated makes activation depend
on filenames and reflection order. Plugins also need a loader-owned way to state
that another plugin must be ready before their own `Load()` callback runs.

## Decision

Plugins declare dependencies by stable plugin ID with
`InsiderPluginDependencyAttribute`. Insider discovers and validates all plugin
types before activation, removes candidates with missing required nodes, and
computes a deterministic dependency-first order.

Required edges must be acyclic. Optional edges influence order when possible but
do not block activation and cannot create a hard cycle. If a required plugin
fails during activation, its dependants are skipped. Unload remains the exact
reverse of successful activation order.

The initial contract does not include plugin version ranges. Plugin versions are
informational until semantic-version validation and a compatibility policy are
defined together.

## Consequences

- Plugin activation no longer depends on file or reflection order.
- Missing requirements and cycles are diagnosed before affected code runs.
- Plugin authors must use stable, globally unique IDs.
- Optional integrations must still tolerate the optional plugin failing during
  its own activation.

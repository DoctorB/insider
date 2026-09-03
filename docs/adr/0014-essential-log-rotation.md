# ADR 0014: Retain only the current and previous log sessions

## Status

Accepted

## Context

Appending every game run to one file makes old and current failures difficult to
separate and allows logs to grow without a bound. A configurable logging stack
would add concepts and dependencies that Insider does not currently need.

The native bootstrap and managed loader must remain independently observable:
native failures can happen before managed code is available.

## Decision

At the start of each process, Insider moves an existing current log to one fixed
previous-session filename:

- `native.log` becomes `native.previous.log`;
- `insider.log` becomes `insider.previous.log`.

An older previous file is replaced. No numbered archives, size limits, time
rules, configuration settings, or external logging packages are introduced.
Native rotation runs once before the first native message; managed rotation runs
when the managed file logger is created.

Rotation is best effort and must never abort the bootstrap.

## Consequences

- Current diagnostics are easy to distinguish from the immediately preceding
  session.
- Retention is fixed at two files per logging layer.
- Users who need longer retention must copy the files outside the game directory.
- Native and managed events remain in separate files rather than one ordered
  stream.

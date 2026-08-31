# Changelog

All notable changes to Insider will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial repository structure and project governance.
- Managed plugin contracts and chainloader foundation.
- Unity runtime inspection CLI.
- Doorstop-compatible managed bootstrap entry point.
- Archived Insider v1 source for historical reference.
- Insider-owned Windows x64 `version.dll` bootstrap for Unity Mono.
- Safe CLI install, status, and uninstall commands with SHA-256 manifests.
- Windows x64 CI package artifact containing a framework-dependent .NET 10 CLI.
- Native fake-Mono fixture covering the bootstrap call sequence, delayed domain
  availability, and managed invocation failures.
- Managed bootstrap integration fixture covering runtime detection, plugin
  discovery, lifecycle callbacks, logging, and unsupported runtimes.
- Deterministic plugin dependency catalog with exact identity resolution and
  failure-closed diagnostics for missing or conflicting assemblies.
- Plugin development guide and dependency-resolution architecture decision.
- Required and optional plugin-ID dependencies with deterministic graph-based
  activation, cycle detection, and failure propagation.
- Simple `MAJOR.MINOR.PATCH` plugin versions and inclusive minimum-version
  requirements, without a range-expression language.

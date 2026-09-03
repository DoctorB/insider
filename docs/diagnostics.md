# CLI diagnostics

Use one command to inspect a game installation before launching it:

```powershell
dotnet insider.dll diagnose "C:\Games\Example\Example.exe"
```

The command is read-only. It does not start the executable, create plugin
instances, invoke `Load()` or `Unload()`, or change any file.

## Report contents

The report has five parts:

1. **Game** shows the executable, Unity layout, scripting backend, architecture,
   and whether it matches Insider's current Windows x64 Unity Mono target.
2. **Installation** verifies `Insider/install.json` and the hashes of every
   loader-owned file.
3. **Plugins** lists each discovered plugin as `Ready`, `Disabled`, or `Problem`.
4. **Disabled plugin IDs** shows the normalized contents of
   `Insider/config/disabled-plugins.txt`.
5. **Problems** collects actionable structural failures in one place.

Dependencies are printed below their owning plugin. Required dependencies must
be present, enabled, structurally valid, outside a required cycle, and at or
above their declared minimum version. Missing, disabled, incompatible, or
broken optional dependencies are shown but remain allowed.

When a plugin declares `MinimumInsiderVersion`, the Plugins section also prints
`Insider: >= MAJOR.MINOR.PATCH`. A malformed requirement or one newer than the
current Insider build marks that plugin as `Problem` without creating it.

For example:

```text
Plugins
  Directory:    C:\Games\Example\Insider\plugins
  Found:        3
  Disabled IDs: 1
  [Ready] com.example.foundation 1.2.0 - Foundation
  [Ready] com.example.gameplay 1.0.0 - Gameplay Tweaks
    Insider:  >= 0.1.0
    Dependency: com.example.foundation >= 1.0.0 (required) - ready (1.2.0)
  [Disabled] com.example.experimental 0.2.0 - Experimental

Problems (0)
  None. The detected configuration is ready for the current Insider build.
```

## Problems and notes

The command returns a problem for any of these conditions:

- missing executable, unrecognized Unity layout, or unsupported backend or
  architecture;
- missing, damaged, modified, or unreadable Insider installation files;
- unreadable managed assemblies or ambiguous managed dependency candidates;
- missing plugin metadata, invalid plugin or minimum versions, duplicate plugin
  IDs, or repeated dependency declarations;
- invalid or unsatisfied minimum Insider versions;
- missing, disabled, incompatible, or already-broken required plugins;
- required dependency cycles.

A disabled ID that no longer matches an installed plugin is a note rather than
a problem. It can be removed with `plugins enable`, but it does not prevent any
discovered plugin from loading.

## Exit codes

- `0`: no structural problem was found;
- `1`: one or more problems are present, or the command could not complete;
- `2`: the command name itself is unknown.

A clean report is intentionally narrower than a compatibility guarantee. It
cannot predict failures inside game-specific plugin code or replace a controlled
runtime test.

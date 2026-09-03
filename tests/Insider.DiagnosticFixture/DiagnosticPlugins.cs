using System;

namespace Insider.DiagnosticFixture;

public abstract class DiagnosticPluginBase : IInsiderPlugin
{
    public void Load(IInsiderContext context) => throw UnexpectedActivation();

    public void Unload() => throw UnexpectedActivation();

    private static Exception UnexpectedActivation() => new InvalidOperationException("Diagnostic fixture code must not run.");
}

[InsiderPlugin("dev.insider.tests.diagnostic-foundation", "Diagnostic Foundation", "1.2.0")]
public sealed class DiagnosticFoundationPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-ready", "Diagnostic Ready", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.diagnostic-foundation", "1.0.0")]
public sealed class DiagnosticReadyPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-optional", "Diagnostic Optional", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.not-installed", optional: true)]
public sealed class DiagnosticOptionalPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin(
    "dev.insider.tests.diagnostic-compatible-insider",
    "Diagnostic Compatible Insider",
    "1.0.0",
    MinimumInsiderVersion = "0.1.0")]
public sealed class DiagnosticCompatibleInsiderPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin(
    "dev.insider.tests.diagnostic-needs-newer-insider",
    "Diagnostic Needs Newer Insider",
    "1.0.0",
    MinimumInsiderVersion = "999.0.0")]
public sealed class DiagnosticNeedsNewerInsiderPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-broken", "Diagnostic Broken", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.missing")]
public sealed class DiagnosticBrokenPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-disabled", "Diagnostic Disabled", "1.0.0")]
public sealed class DiagnosticDisabledPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-needs-disabled", "Diagnostic Needs Disabled", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.diagnostic-disabled")]
public sealed class DiagnosticNeedsDisabledPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-needs-newer", "Diagnostic Needs Newer", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.diagnostic-foundation", "2.0.0")]
public sealed class DiagnosticNeedsNewerPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-duplicate", "Diagnostic Duplicate A", "1.0.0")]
public sealed class DiagnosticDuplicateAPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-duplicate", "Diagnostic Duplicate B", "1.0.0")]
public sealed class DiagnosticDuplicateBPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-cycle-a", "Diagnostic Cycle A", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.diagnostic-cycle-b")]
public sealed class DiagnosticCycleAPlugin : DiagnosticPluginBase
{
}

[InsiderPlugin("dev.insider.tests.diagnostic-cycle-b", "Diagnostic Cycle B", "1.0.0")]
[InsiderPluginDependency("dev.insider.tests.diagnostic-cycle-a")]
public sealed class DiagnosticCycleBPlugin : DiagnosticPluginBase
{
}

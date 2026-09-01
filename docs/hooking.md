# Managed hooking guide

This document is the practical reference for Insider's managed hooking API. It
covers every behavior currently exposed by `IInsiderHookService`, with examples
that can be adapted to a plugin.

The backend is currently experimental and targets Windows x64 games using the
Unity Mono scripting backend. IL2CPP is not supported.

Types such as `GameRules`, `Enemy`, `CombatStats`, and `Actor` in the snippets
are illustrative game types; replace them with the exact types from the target
assembly.

## The contract

Plugins create detours through the context supplied to `Load`:

```csharp
IDisposable handle = context.Hooks.Detour(target, replacement);
```

- `target` is a reflected `MethodInfo` or instance `ConstructorInfo`.
- `replacement` is a delegate whose signature must match exactly.
- The detour is active when `Detour` returns.
- Disposing the handle removes only that detour. Disposal is idempotent; after
  a removal failure, disposing the same handle again retries it.
- Insider owns every handle created through a plugin context and removes any
  remaining detours after `Unload()` or a failed `Load()`.

The signature mapping is:

| Target | Replacement signature |
| --- | --- |
| `static R Method(A)` | `R Replacement(A)` |
| `R Type.Method(A)` on a class | `R Replacement(Type self, A)` |
| `R Type.Method(A)` on a struct | `R Replacement(ref Type self, A)` |
| `Type.ctor(A)` on a class | `void Replacement(Type self, A)` |

Declared `ref`, `out`, and `in` parameters keep their by-reference position in
every replacement and original-call delegate. A by-reference return must also
remain by reference.

Any replacement may prepend an original-call delegate with the same return type
and parameters shown in the table. That delegate advances to the next detour in
the chain and eventually to the original member.

## Minimal plugin lifecycle

Keeping the returned handle lets a plugin remove its hook early. Explicit
cleanup in `Unload` is clear and remains safe even though the loader also
guarantees cleanup:

```csharp
using System;
using System.Reflection;
using Insider;

[InsiderPlugin("com.example.simple-hook", "Simple Hook", "1.0.0")]
public sealed class SimpleHookPlugin : IInsiderPlugin
{
    private IDisposable? _hook;

    public void Load(IInsiderContext context)
    {
        var target = typeof(GameRules).GetMethod(
            nameof(GameRules.Compute),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int) },
            modifiers: null)
            ?? throw new InvalidOperationException("Target method not found.");

        _hook = context.Hooks.Detour(target, (ComputeHook)Replacement);
    }

    public void Unload()
    {
        _hook?.Dispose();
        _hook = null;
    }

    private delegate int ComputeHook(int value);

    private static int Replacement(int value)
    {
        return 42;
    }
}
```

A direct replacement such as this one does not call the original method.

## Wrapping the original method

Prepend an original-call delegate when the plugin should preserve and extend
existing behavior:

```csharp
private delegate int ComputeOriginal(int value);
private delegate int ComputeHook(ComputeOriginal original, int value);

private static int Replacement(ComputeOriginal original, int value)
{
    var originalResult = original(value);
    return originalResult + 10;
}
```

Invoke `original` synchronously while the replacement is running. Do not store
the delegate or invoke it later from another thread.

## By-reference parameters and returns

### Ref and out parameters

Methods that mutate arguments in place or follow the `TryGet` pattern can be
wrapped without copying their values. Mirror each `ref` and `out` parameter in
both delegate types and pass the modifiers again when calling the original:

```csharp
private delegate bool TransformOriginal(ref int value, out int output);
private delegate bool TransformHook(
    TransformOriginal original,
    ref int value,
    out int output);

private static bool Replacement(
    TransformOriginal original,
    ref int value,
    out int output)
{
    value++;
    var succeeded = original(ref value, out output);
    output += 10;
    return succeeded;
}
```

At the CLR level, `ref` and `out` are both managed by-reference parameter types.
Use the same modifier as the target in plugin source so intent and definite
assignment remain clear. Insider's mismatch diagnostics render either form as
`byref ElementType`.

### In parameters

Readonly by-reference parameters use `in` in both delegates and at the call
site:

```csharp
private delegate float DistanceOriginal(in Vector3 left, in Vector3 right);
private delegate float DistanceHook(
    DistanceOriginal original,
    in Vector3 left,
    in Vector3 right);

private static float Replacement(
    DistanceOriginal original,
    in Vector3 left,
    in Vector3 right)
{
    return original(in left, in right);
}
```

The CLR represents `ref`, `out`, and `in` using managed by-reference parameter
types. Keep the source modifier identical to the target even when two forms are
ABI-compatible; it preserves intent and lets the C# compiler enforce the right
read/write rules.

### By-reference returns

A target that returns a managed reference needs `ref` on the original-call
delegate, replacement delegate, replacement method, and returned expression:

```csharp
private delegate ref int ScoreOriginal(ScoreTable self, int index);
private delegate ref int ScoreHook(
    ScoreOriginal original,
    ScoreTable self,
    int index);

private static ref int Replacement(
    ScoreOriginal original,
    ScoreTable self,
    int index)
{
    return ref original(self, index);
}
```

Returning the value instead of the managed reference is a different signature
and is rejected before the backend applies the detour.

## Closed generic methods

Open generic definitions cannot be executed and are rejected. A fully
constructed method is an eligible concrete hook target. Reflect the definition,
close it with the exact runtime type arguments, and use those concrete types in
the delegates:

```csharp
private IDisposable? _hook;

private delegate Enemy? FindEnemyOriginal(GameCache self, string id);
private delegate Enemy? FindEnemyHook(
    FindEnemyOriginal original,
    GameCache self,
    string id);

private void Install(IInsiderContext context)
{
    var definition = typeof(GameCache).GetMethod(
        nameof(GameCache.Find),
        BindingFlags.Public | BindingFlags.Instance,
        binder: null,
        types: new[] { typeof(string) },
        modifiers: null)
        ?? throw new InvalidOperationException("Generic target not found.");

    if (!definition.IsGenericMethodDefinition)
    {
        throw new InvalidOperationException("Expected a generic definition.");
    }

    var target = definition.MakeGenericMethod(typeof(Enemy));
    _hook = context.Hooks.Detour(target, (FindEnemyHook)Replacement);
}

private static Enemy? Replacement(
    FindEnemyOriginal original,
    GameCache self,
    string id)
{
    return original(self, id);
}
```

The same rule applies when the declaring type is generic: every type parameter
on both the declaring type and method must already be closed. Mono runtimes may
share generated code between some generic instantiations, so validate isolation
between type arguments in the exact Unity runtime and game being targeted. The
current fixture does not yet provide that compatibility evidence.

## Reference-type instance methods

An instance method declared on a class receives its declaring type as `self`
before the method's declared parameters:

```csharp
private delegate int DamageOriginal(Enemy self, int amount);
private delegate int DamageHook(
    DamageOriginal original,
    Enemy self,
    int amount);

private static int Replacement(
    DamageOriginal original,
    Enemy self,
    int amount)
{
    var adjustedAmount = Math.Max(1, amount / 2);
    return original(self, adjustedAmount);
}
```

The `self` parameter is required even when the replacement does not use it.

## Virtual methods and overrides

A virtual base method and each override are separate hook targets. Reflect the
exact implementation with `BindingFlags.DeclaredOnly`, then use that declaring
type as `self` in its delegates:

```csharp
var baseTarget = typeof(Enemy).GetMethod(
    nameof(Enemy.CalculateDamage),
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
    ?? throw new InvalidOperationException("Base target not found.");

var overrideTarget = typeof(BossEnemy).GetMethod(
    nameof(BossEnemy.CalculateDamage),
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
    ?? throw new InvalidOperationException("Override target not found.");

_baseHook = context.Hooks.Detour(
    baseTarget,
    (EnemyDamageHook)ReplaceEnemyDamage);
_overrideHook = context.Hooks.Detour(
    overrideTarget,
    (BossDamageHook)ReplaceBossDamage);
```

The delegate types are distinct because their `self` types are distinct:

```csharp
private delegate int EnemyDamageOriginal(Enemy self, int amount);
private delegate int EnemyDamageHook(
    EnemyDamageOriginal original,
    Enemy self,
    int amount);

private delegate int BossDamageOriginal(BossEnemy self, int amount);
private delegate int BossDamageHook(
    BossDamageOriginal original,
    BossEnemy self,
    int amount);
```

Hooking the base implementation does not automatically hook an override, and
hooking an override does not change the base implementation. If an override
explicitly calls the base implementation, that base call still reaches the base
hook. Each returned handle removes only its own implementation's detour.

## Value-type instance methods

An instance method declared on a struct receives `self` by reference. Both the
replacement and the original-call delegate must use `ref` exactly:

```csharp
private delegate int ApplyOriginal(ref CombatStats self, int amount);
private delegate int ApplyHook(
    ApplyOriginal original,
    ref CombatStats self,
    int amount);

private static int Replacement(
    ApplyOriginal original,
    ref CombatStats self,
    int amount)
{
    var result = original(ref self, amount * 2);
    return result + 10;
}
```

Using `ref self` preserves mutations made by the original method or the
replacement. Passing the struct by value is rejected because it would operate
on a copy.

## Instance constructors

Class constructors use a `void` signature and receive the new object as `self`:

```csharp
private delegate void ActorConstructorOriginal(Actor self, int health);
private delegate void ActorConstructorHook(
    ActorConstructorOriginal original,
    Actor self,
    int health);

private static void Replacement(
    ActorConstructorOriginal original,
    Actor self,
    int health)
{
    original(self, Math.Max(1, health));
    self.IsModded = true;
}
```

Reflect and install the constructor like this:

```csharp
var target = typeof(Actor).GetConstructor(
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
    binder: null,
    types: new[] { typeof(int) },
    modifiers: null)
    ?? throw new InvalidOperationException("Target constructor not found.");

_hook = context.Hooks.Detour(
    target,
    (ActorConstructorHook)Replacement);
```

Static constructors and value-type constructors are not supported.

## Multiple detours on one target

Multiple detours form a continuation chain. Each original-call delegate invokes
the next node, and each handle removes only its own node:

```csharp
private IDisposable? _addTen;
private IDisposable? _addTwenty;

public void Load(IInsiderContext context)
{
    var target = GetTarget();
    _addTen = context.Hooks.Detour(target, (ComputeHook)AddTen);
    _addTwenty = context.Hooks.Detour(target, (ComputeHook)AddTwenty);
}

private delegate int ComputeOriginal();
private delegate int ComputeHook(ComputeOriginal original);

private static int AddTen(ComputeOriginal original)
{
    return original() + 10;
}

private static int AddTwenty(ComputeOriginal original)
{
    return original() + 20;
}
```

If the original returns `7`, both hooks above produce `37`. Disposing
`_addTwenty` leaves `_addTen` active and changes the result to `17`; disposing
both restores `7`.

Do not depend on execution order between different plugins. Insider guarantees
ownership and independent removal, but does not define inter-plugin ordering.

## Loader ownership and failure cleanup

Every `context.Hooks.Detour` call is scoped to the plugin that made it:

- A successful plugin may dispose a handle whenever it no longer needs the hook.
- Remaining hooks stay active during the plugin's `Unload()` callback and are
  removed immediately afterward.
- If `Load()` throws after creating hooks, Insider removes those hooks.
- Removing one plugin's hooks does not remove another plugin's nodes from the
  same target chain.

Handles become disposed only after the runtime confirms removal. A removal
failure leaves the handle tracked and retryable instead of silently orphaning a
possibly active detour. Loader cleanup attempts every owned handle even if one
fails, then reports the failures together.

Hooks created directly through third-party APIs are outside this ownership
model and cannot be cleaned up by Insider.

## Errors and diagnostics

Contract validation happens before runtime patching:

- Invalid or mismatched signatures throw `ArgumentException`.
- Unsupported targets throw `NotSupportedException`.
- A backend failure while applying or removing a valid detour throws
  `InsiderHookException` and retains the original exception in
  `InnerException`.

Signature errors identify the reflected target, the actual delegate signature,
and the required signature. Generic, array, pointer, and managed by-reference
types are formatted without leaking a MonoMod type into plugin code. A
replacement delegate must also contain exactly one invocation target;
multicast delegates are rejected because their hook semantics would be
ambiguous.

## Hooking late Unity assemblies

Use the `MethodInfo` from the assembly instance Unity actually loaded. Game
assemblies such as `Assembly-CSharp` may arrive after the plugin. Do not call
`Assembly.Load` to force an early private copy; inspect loaded assemblies and
observe `AppDomain.AssemblyLoad`:

The following pattern reuses the `ComputeHook` delegate and `Replacement`
method from the minimal static-method example above.

```csharp
private IInsiderContext? _context;
private IDisposable? _gameHook;

public void Load(IInsiderContext context)
{
    _context = context;
    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        TryHookGameAssembly(assembly);
    }
}

public void Unload()
{
    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    _gameHook?.Dispose();
    _gameHook = null;
    _context = null;
}

private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
{
    try
    {
        TryHookGameAssembly(args.LoadedAssembly);
    }
    catch (Exception exception)
    {
        _context?.Logger.Error("Could not install the game hook.", exception);
    }
}

private void TryHookGameAssembly(Assembly assembly)
{
    if (_gameHook is not null ||
        assembly.GetName().Name != "Assembly-CSharp")
    {
        return;
    }

    var type = assembly.GetType("Example.GameRules", throwOnError: true)
        ?? throw new InvalidOperationException("Target type not found.");
    var target = type.GetMethod(
        "Compute",
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(int) },
        modifiers: null)
        ?? throw new InvalidOperationException("Target method not found.");

    _gameHook = (_context
        ?? throw new InvalidOperationException("Plugin context unavailable."))
        .Hooks.Detour(target, (ComputeHook)Replacement);
}
```

For overloaded members, always supply the binding flags and parameter types as
shown above. A name-only reflection lookup can select the wrong overload or fail
with an ambiguous-match exception.

## Supported surface and current limits

The current managed backend supports:

- Static methods.
- Declared `ref`, `out`, and `in` parameters.
- Managed by-reference returns.
- Fully constructed generic methods and declaring types.
- Reference-type instance methods with `self`.
- Virtual base methods and overrides as independently targeted implementations.
- Value-type instance methods with `ref self`.
- Reference-type instance constructors.
- Direct replacements and synchronous original-call continuations.
- Multiple independently removable detours on one target.
- Idempotent early removal, retryable removal failures, and loader-owned
  cleanup.
- Stable `InsiderHookException` wrapping runtime apply and removal failures.

It deliberately rejects or does not expose:

- Abstract methods.
- Open generic methods.
- Multicast replacement delegates.
- Variable-argument methods.
- Static constructors and value-type constructors.
- IL rewriting, HookGen, ordering controls, and native detours.
- IL2CPP targets.

See [compatibility.md](compatibility.md) for the evidence behind current support
claims and [testing.md](testing.md) for the exact automated and Unity fixtures.

## Documentation maintenance

This is the canonical usage guide for `IInsiderHookService`. Any change to the
public hooking contract, signature rules, lifecycle, supported targets, or
runtime evidence must update this document in the same pull request.

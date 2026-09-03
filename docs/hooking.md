# Runtime hooking guide

This document is the practical reference for Insider's hooking API. It covers
managed detours, IL hooks, and the small native-detour operation exposed by
`IInsiderHookService`.

Managed detours and IL hooks are currently experimental and target Windows x64
games using the Unity Mono scripting backend. The essential Windows x64 IL2CPP
backend exposes native detours only; it does not turn compiled game code into
managed `MethodBase` or `ILContext` targets.

Types such as `GameRules`, `Enemy`, `CombatStats`, and `Actor` in the snippets
are illustrative game types; replace them with the exact types from the target
assembly.

The service has three operations:

```csharp
IDisposable detour = context.Hooks.Detour(target, replacement);
IDisposable ilHook = context.Hooks.ModifyIl(target, manipulator);
IDisposable nativeDetour = context.Hooks.DetourNative(address, nativeReplacement);
```

All are active when the call returns, produce independently removable handles,
and are owned by the plugin context. Use `Detour` when a delegate
can replace or wrap the whole method. Use `ModifyIl` when a precise change must
be made inside the original method body. Use `DetourNative` only after resolving
and verifying an exact native ABI.

## Managed detour contract

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

## Generic targets

MonoMod.RuntimeDetour 25.3.6 does not support generic source methods, including
fully constructed ones. Mono may also share generated code between members of
constructed generic types. Insider therefore fails closed before patching:

- Open generic targets throw `ArgumentException`.
- Closed generic methods and members declared on generic types throw
  `NotSupportedException`.

Generic types remain valid as ordinary parameter or return types of a
non-generic member declared on a non-generic type. The restriction concerns the
hook target itself.

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

## IL hooks

`ModifyIl` runs a callback against MonoMod's `ILContext` and installs the
resulting rewritten body:

```csharp
using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;

private IDisposable? _ilHook;

public void Load(IInsiderContext context)
{
    var target = typeof(GameRules).GetMethod(
        nameof(GameRules.Compute),
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(int) },
        modifiers: null)
        ?? throw new InvalidOperationException("Target method not found.");

    _ilHook = context.Hooks.ModifyIl(target, RewriteCompute);
}

private static void RewriteCompute(ILContext il)
{
    var cursor = new ILCursor(il);
    if (!cursor.TryGotoNext(
        MoveType.Before,
        instruction => instruction.MatchLdcI4(7)))
    {
        throw new InvalidOperationException(
            "Expected Compute constant was not found; the game may have changed.");
    }

    cursor.Remove();
    cursor.Emit(OpCodes.Ldc_I4, 42);
}
```

This example replaces the first integer constant `7` with `42`. Real game IL
should be matched with enough surrounding instructions to identify one stable
location; a single constant is intentionally only a minimal example. Fail the
manipulator when the expected pattern is absent instead of silently patching a
different location after a game update.

The callback has the complete MonoMod/Cecil surface. It can inspect and edit
instructions, locals, branches, labels, and exception handlers, and it can emit
calls or delegates. For example, a static callback can be inserted immediately
before every return without consuming a non-void return value already on the
evaluation stack:

```csharp
private static void AddReturnCallbacks(ILContext il)
{
    var cursor = new ILCursor(il);
    while (cursor.TryGotoNext(
        MoveType.Before,
        instruction => instruction.OpCode == OpCodes.Ret))
    {
        cursor.EmitDelegate<Action>(OnReturning);
        cursor.Index++;
    }
}

private static void OnReturning()
{
    // Keep injected callbacks small and safe for the target thread.
}
```

An IL hook is lower level than a detour. The manipulator is responsible for
leaving valid IL, a balanced evaluation stack, valid branch targets, and valid
exception regions. Insider validates the reflected target and owns the runtime
hook, but it cannot prove the semantic correctness of arbitrary emitted IL.

### Manipulator lifetime and repeatability

MonoMod may invoke a manipulator again whenever another IL hook is added to or
removed from the same target. A manipulator must therefore:

- produce the same edit whenever it receives the same input body;
- keep side effects out of the manipulation callback;
- avoid storing `ILContext`, `ILCursor`, Cecil instructions, or labels after the
  callback returns;
- fail clearly when its expected pattern is absent;
- avoid depending on execution order between different plugins.

Injected delegates may refer to static methods or deliberately capture plugin
state. Any captured state remains reachable while the IL hook is active, so the
handle must be removed before that state is considered unloaded.

### Multiple IL hooks and detours

Multiple IL hooks may target the same method. Each manipulator contributes to
the rebuilt body, and disposing one handle rebuilds the target without that
manipulator while leaving the others active. Insider does not expose priority or
ordering controls, so manipulators should locate semantic instruction patterns
instead of relying on absolute indexes or another plugin's output.

Managed detours and IL hooks may also target the same method. Insider owns and
removes both kinds independently, but it deliberately makes no promise about
cross-plugin ordering. Prefer one technique per target inside a plugin unless
the combination is essential and can be validated against the exact game
version.

### IL target validation

`ModifyIl` accepts managed methods and instance constructors, including
value-type instance constructors, only when reflection exposes a readable IL
body. It rejects abstract, external, P/Invoke, runtime-provided, generic, and
variable-argument targets, plus static constructors. A multicast manipulator is
also rejected because its rebuild and failure semantics would be ambiguous.

`ILContext` and Cecil are advanced types supplied by Insider's pinned MonoMod
runtime. Compile against the versions selected by `Insider.Abstractions`, but
do not redistribute `MonoMod.*`, `Mono.Cecil*`, or
`Insider.Abstractions.dll` with a plugin. The loader owns those assemblies and
all plugins must use the process-wide copies.

## Native detours

`DetourNative(IntPtr target, Delegate replacement)` applies the same ownership
and cleanup model to a native function address. The target must be non-zero and
the replacement must be one unmanaged-compatible, single-cast delegate.
Insider can validate those structural rules, but it cannot infer or validate the
native function signature.

On IL2CPP, obtain addresses from `context.Il2Cpp.ResolveExport(...)` or
`ResolveMethod(...)`. Addresses are valid only in the current process. The
replacement must match the target's calling convention, return value, argument
widths, pointer levels, instance/static shape, and hidden IL2CPP arguments. A
mismatch can corrupt memory or terminate the game.

The full resolution and native replacement example is in the
[essential IL2CPP guide](il2cpp.md). Native detours are also available as a
low-level operation on Mono, but managed game methods should normally use the
safer reflected `Detour` contract.

## Loader ownership and failure cleanup

Every `context.Hooks.Detour`, `ModifyIl`, and `DetourNative` call is scoped to
the plugin that made it:

- A successful plugin may dispose a handle whenever it no longer needs the hook.
- Remaining managed detours, native detours, and IL hooks stay active during the plugin's `Unload()`
  callback and are removed immediately afterward.
- If `Load()` throws after creating hooks, Insider removes those hooks.
- Removing one plugin's hooks does not remove another plugin's nodes from the
  same target chain.

Handles become disposed only after the runtime confirms removal. A removal
failure leaves the handle tracked and retryable instead of silently orphaning a
possibly active hook. Loader cleanup attempts every owned handle even if one
fails, then reports the failures together.

Hooks created directly through third-party APIs are outside this ownership
model and cannot be cleaned up by Insider.

## Errors and diagnostics

Contract validation happens before runtime patching:

- Invalid or mismatched signatures throw `ArgumentException`.
- Unsupported targets throw `NotSupportedException`.
- A backend failure while applying or removing a valid managed detour, native
  detour, or IL hook throws
  `InsiderHookException` and retains the original exception in
  `InnerException`.

Detour signature errors identify the reflected target, the actual delegate
signature, and the required signature. Generic, array, pointer, and managed
by-reference types are formatted without leaking a MonoMod type into detour
signatures. A replacement delegate must also contain exactly one invocation
target; multicast delegates are rejected because their hook semantics would be
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

The current managed detour backend supports:

- Static methods.
- Declared `ref`, `out`, and `in` parameters.
- Managed by-reference returns.
- Reference-type instance methods with `self`.
- Virtual base methods and overrides as independently targeted implementations.
- Value-type instance methods with `ref self`.
- Reference-type instance constructors.
- Direct replacements and synchronous original-call continuations.
- Multiple independently removable detours on one target.
- Idempotent early removal, retryable removal failures, and loader-owned
  cleanup.
- Stable `InsiderHookException` wrapping runtime apply and removal failures.

The native backend supports detours from verified process-local addresses and
single-cast unmanaged-compatible delegates. It does not validate the target ABI.

The IL backend supports:

- Managed methods and instance constructors with readable IL bodies.
- The complete MonoMod `ILContext`, `ILCursor`, and Cecil instruction surface.
- Multiple independently removable manipulators on one target.
- Coexistence with managed detours on the same target without an ordering
  guarantee.
- Idempotent early removal, retryable failures, reverse-order plugin cleanup,
  and the same stable `InsiderHookException` boundary as detours.

It deliberately rejects or does not expose:

- Abstract methods.
- Open and closed generic methods, and members declared on generic types.
- Multicast replacement delegates.
- Variable-argument methods.
- Static constructors; value-type constructors remain unsupported by `Detour`
  but may be rewritten through `ModifyIl` when they expose a body.
- HookGen and ordering controls.
- Managed detours or IL hooks against IL2CPP game methods.
- Automatic IL2CPP proxies, overload disambiguation beyond name and parameter
  count, or ABI validation.

See [compatibility.md](compatibility.md) for the evidence behind current support
claims and [testing.md](testing.md) for the exact automated and Unity fixtures.

## Documentation maintenance

This is the canonical usage guide for `IInsiderHookService`. Any change to the
public hooking contract, signature or IL rules, lifecycle, supported targets,
or runtime evidence must update this document in the same pull request.

using System;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace Insider.Hooking;

public sealed class RuntimeDetourHookService : IInsiderHookService
{
    public IDisposable Detour(MethodInfo target, Delegate replacement)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        if (target.IsAbstract)
        {
            throw new ArgumentException("Abstract methods cannot be detoured.", nameof(target));
        }

        if (target.ContainsGenericParameters || replacement.Method.ContainsGenericParameters)
        {
            throw new ArgumentException("Open generic methods cannot be detoured.", nameof(target));
        }

        return new Hook(target, replacement);
    }
}

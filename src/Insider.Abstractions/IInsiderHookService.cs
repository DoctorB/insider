using System;
using System.Reflection;

namespace Insider;

/// <summary>
/// Creates loader-owned managed method detours.
/// </summary>
public interface IInsiderHookService
{
    /// <summary>
    /// Replaces or wraps a managed method until the returned handle is disposed.
    /// </summary>
    /// <param name="target">The managed method to detour.</param>
    /// <param name="replacement">
    /// A delegate with the target signature, including <c>self</c> for instance
    /// methods and optionally preceded by an original-call delegate.
    /// </param>
    /// <returns>A handle that removes the detour when disposed.</returns>
    IDisposable Detour(MethodInfo target, Delegate replacement);
}

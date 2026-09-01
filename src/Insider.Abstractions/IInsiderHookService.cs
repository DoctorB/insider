using System;
using System.Reflection;

namespace Insider;

/// <summary>
/// Creates loader-owned managed method and constructor detours.
/// </summary>
public interface IInsiderHookService
{
    /// <summary>
    /// Replaces or wraps a managed method or instance constructor until the returned handle is disposed.
    /// </summary>
    /// <param name="target">The managed method or instance constructor to detour.</param>
    /// <param name="replacement">
    /// A delegate with the target signature, including <c>self</c> for instance
    /// methods and constructors, and optionally preceded by an original-call delegate.
    /// </param>
    /// <returns>A handle that removes the detour when disposed.</returns>
    IDisposable Detour(MethodBase target, Delegate replacement);
}

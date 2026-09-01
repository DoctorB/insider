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
    /// <param name="target">The exact managed method implementation or instance constructor to detour.</param>
    /// <param name="replacement">
    /// A delegate with the target signature, including <c>self</c> for instance
    /// methods and constructors. Value-type methods receive <c>self</c> by
    /// reference, and declared by-reference parameters and returns remain by
    /// reference. The signature may be preceded by an original-call delegate.
    /// </param>
    /// <returns>
    /// An idempotent handle that removes the detour when disposed. If removal
    /// fails, disposing the handle again retries the operation.
    /// </returns>
    /// <exception cref="InsiderHookException">
    /// The runtime backend could not apply the detour. A returned handle throws
    /// the same exception type if the backend cannot remove it.
    /// </exception>
    IDisposable Detour(MethodBase target, Delegate replacement);
}

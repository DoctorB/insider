using System;
using System.Reflection;
using MonoMod.Cil;

namespace Insider;

/// <summary>
/// Creates loader-owned managed detours, native detours, and IL hooks.
/// </summary>
public interface IInsiderHookService
{
    /// <summary>
    /// Replaces or wraps a native function until the returned handle is disposed.
    /// </summary>
    /// <param name="target">The non-zero native function address to detour.</param>
    /// <param name="replacement">
    /// An unmanaged-compatible delegate. It may be preceded by an original-call
    /// delegate with the same native signature.
    /// </param>
    /// <returns>An idempotent handle that removes the native detour.</returns>
    /// <exception cref="InsiderHookException">
    /// The runtime backend could not apply or remove the native detour.
    /// </exception>
    IDisposable DetourNative(IntPtr target, Delegate replacement);

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

    /// <summary>
    /// Rewrites the IL of a managed method or instance constructor until the returned handle is disposed.
    /// </summary>
    /// <param name="target">The exact managed method implementation or instance constructor to rewrite.</param>
    /// <param name="manipulator">
    /// A deterministic callback that edits the supplied IL context. The backend
    /// may invoke it again when the hook chain for the target is rebuilt.
    /// </param>
    /// <returns>
    /// An idempotent handle that removes the IL hook when disposed. If removal
    /// fails, disposing the handle again retries the operation.
    /// </returns>
    /// <exception cref="InsiderHookException">
    /// The runtime backend could not apply or remove the IL hook.
    /// </exception>
    IDisposable ModifyIl(MethodBase target, Action<ILContext> manipulator);
}

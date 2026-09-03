using System;

namespace Insider;

/// <summary>
/// Resolves the small native IL2CPP surface exposed by the current game.
/// </summary>
public interface IInsiderIl2CppRuntime
{
    /// <summary>
    /// Gets whether the IL2CPP domain is available for metadata resolution.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Resolves an exported function from the loaded GameAssembly module.
    /// </summary>
    /// <exception cref="MissingMethodException">The export is not available.</exception>
    IntPtr ResolveExport(string name);

    /// <summary>
    /// Resolves the native IL2CPP MethodInfo pointer for an exact type, method
    /// name, and parameter count.
    /// </summary>
    /// <exception cref="MissingMemberException">The requested metadata cannot be found.</exception>
    IntPtr ResolveMethodInfo(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName,
        int parameterCount);

    /// <summary>
    /// Resolves the native code pointer stored by the requested IL2CPP method.
    /// The pointer and its ABI are specific to the current process, game, and
    /// Unity IL2CPP version.
    /// </summary>
    /// <exception cref="MissingMemberException">The requested metadata cannot be found.</exception>
    /// <exception cref="NotSupportedException">The method has no hookable native code pointer.</exception>
    IntPtr ResolveMethod(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName,
        int parameterCount);
}

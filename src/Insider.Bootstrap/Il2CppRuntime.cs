using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Insider.Bootstrap;

internal sealed class Il2CppRuntime : IInsiderIl2CppRuntime
{
    private const string GameAssemblyName = "GameAssembly.dll";
    private readonly IntPtr _gameAssembly;
    private readonly DomainGet _domainGet;

    private Il2CppRuntime(IntPtr gameAssembly)
    {
        _gameAssembly = gameAssembly;
        _domainGet = ResolveDelegate<DomainGet>("il2cpp_domain_get");
    }

    public bool IsReady => _domainGet() != IntPtr.Zero;

    public static Il2CppRuntime WaitUntilReady(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var module = GetModuleHandle(GameAssemblyName);
        if (module == IntPtr.Zero)
        {
            throw new InvalidOperationException($"The loaded process does not contain '{GameAssemblyName}'.");
        }

        var runtime = new Il2CppRuntime(module);
        var deadline = DateTime.UtcNow + timeout;
        while (!runtime.IsReady)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the IL2CPP domain to become available.");
            }

            Thread.Sleep(50);
        }

        return runtime;
    }

    public IntPtr ResolveExport(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An IL2CPP export name is required.", nameof(name));
        }

        var address = GetProcAddress(_gameAssembly, name.Trim());
        if (address == IntPtr.Zero)
        {
            throw new MissingMethodException(
                $"'{GameAssemblyName}' does not export '{name.Trim()}'.");
        }

        return address;
    }

    public IntPtr ResolveMethodInfo(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName,
        int parameterCount)
    {
        RequireName(assemblyName, nameof(assemblyName));
        RequireName(typeName, nameof(typeName));
        RequireName(methodName, nameof(methodName));
        if (parameterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameterCount));
        }

        var domain = _domainGet();
        if (domain == IntPtr.Zero)
        {
            throw new InvalidOperationException("The IL2CPP domain is not ready.");
        }

        var assemblies = ResolveDelegate<DomainGetAssemblies>("il2cpp_domain_get_assemblies");
        var assemblyGetImage = ResolveDelegate<AssemblyGetImage>("il2cpp_assembly_get_image");
        var imageGetName = ResolveDelegate<ImageGetName>("il2cpp_image_get_name");
        var classFromName = ResolveDelegate<ClassFromName>("il2cpp_class_from_name");
        var classGetMethod = ResolveDelegate<ClassGetMethodFromName>("il2cpp_class_get_method_from_name");

        var assemblyArray = assemblies(domain, out var countValue);
        var count = checked((int)countValue.ToUInt64());
        if (count > 0 && assemblyArray == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "The IL2CPP domain returned an invalid assembly catalog.");
        }

        var expectedAssembly = NormalizeAssemblyName(assemblyName);
        for (var index = 0; index < count; index++)
        {
            var assembly = Marshal.ReadIntPtr(assemblyArray, checked(index * IntPtr.Size));
            var image = assemblyGetImage(assembly);
            if (image == IntPtr.Zero)
            {
                continue;
            }

            var actualName = Marshal.PtrToStringAnsi(imageGetName(image));
            if (!string.Equals(NormalizeAssemblyName(actualName), expectedAssembly, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var klass = classFromName(image, namespaceName ?? string.Empty, typeName);
            if (klass == IntPtr.Zero)
            {
                throw new MissingMemberException(
                    $"IL2CPP type '{FormatType(namespaceName, typeName)}' was not found in '{assemblyName}'.");
            }

            var method = classGetMethod(klass, methodName, parameterCount);
            if (method == IntPtr.Zero)
            {
                throw new MissingMethodException(
                    $"IL2CPP method '{FormatType(namespaceName, typeName)}.{methodName}' with " +
                    $"{parameterCount} parameter(s) was not found in '{assemblyName}'.");
            }

            return method;
        }

        throw new MissingMemberException($"IL2CPP assembly '{assemblyName}' was not found in the current domain.");
    }

    public IntPtr ResolveMethod(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName,
        int parameterCount)
    {
        var methodInfo = ResolveMethodInfo(
            assemblyName,
            namespaceName,
            typeName,
            methodName,
            parameterCount);
        var methodPointer = Marshal.ReadIntPtr(methodInfo);
        if (methodPointer == IntPtr.Zero)
        {
            throw new NotSupportedException(
                $"IL2CPP method '{FormatType(namespaceName, typeName)}.{methodName}' has no native code pointer.");
        }

        return methodPointer;
    }

    private TDelegate ResolveDelegate<TDelegate>(string name)
        where TDelegate : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ResolveExport(name));
    }

    private static string NormalizeAssemblyName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        return value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? value.Substring(0, value.Length - 4)
            : value;
    }

    private static string FormatType(string? namespaceName, string typeName)
    {
        return string.IsNullOrWhiteSpace(namespaceName)
            ? typeName
            : namespaceName + "." + typeName;
    }

    private static void RequireName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty name is required.", parameterName);
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DomainGet();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DomainGetAssemblies(IntPtr domain, out UIntPtr size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AssemblyGetImage(IntPtr assembly);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ImageGetName(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr ClassFromName(IntPtr image, string namespaceName, string typeName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr ClassGetMethodFromName(IntPtr klass, string methodName, int parameterCount);
}

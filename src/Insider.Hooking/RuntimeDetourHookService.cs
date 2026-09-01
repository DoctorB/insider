using System;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace Insider.Hooking;

public sealed class RuntimeDetourHookService : IInsiderHookService
{
    public IDisposable Detour(MethodBase target, Delegate replacement)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        if (target is not MethodInfo && target is not ConstructorInfo)
        {
            throw new NotSupportedException("Only managed methods and constructors can be detoured.");
        }

        if (target is ConstructorInfo constructor && constructor.IsStatic)
        {
            throw new NotSupportedException("Static constructors are not supported by the Insider hook contract.");
        }

        if (target.IsAbstract)
        {
            throw new ArgumentException("Abstract methods cannot be detoured.", nameof(target));
        }

        if (target.ContainsGenericParameters)
        {
            throw new ArgumentException("Open generic methods cannot be detoured.", nameof(target));
        }

        if ((target is MethodInfo method && method.IsGenericMethod) ||
            (target.DeclaringType?.IsGenericType ?? false))
        {
            throw new NotSupportedException(
                "Generic methods and members declared on generic types are not supported by the current RuntimeDetour backend.");
        }

        if (replacement.Method.ContainsGenericParameters)
        {
            throw new ArgumentException("Open generic replacement methods cannot be used as detours.", nameof(replacement));
        }

        if (replacement.GetInvocationList().Length != 1)
        {
            throw new ArgumentException("A replacement delegate must contain exactly one invocation target.", nameof(replacement));
        }

        if ((target.CallingConvention & CallingConventions.VarArgs) != 0)
        {
            throw new NotSupportedException("Variable-argument methods are not supported by the Insider hook contract.");
        }

        ValidateSignature(target, replacement);

        var targetName = FormatTarget(target);
        try
        {
            return new RuntimeDetourHandle(new Hook(target, replacement), targetName);
        }
        catch (Exception exception)
        {
            throw new InsiderHookException(
                $"Could not apply managed detour to '{targetName}'.",
                exception);
        }
    }

    private static void ValidateSignature(MethodBase target, Delegate replacement)
    {
        var expectedParameters = GetExpectedParameters(target);
        var returnType = GetReturnType(target);
        var replacementInvoke = GetDelegateInvoke(replacement.GetType());
        var replacementParameters = replacementInvoke.GetParameters();

        var isDirectReplacement =
            replacementInvoke.ReturnType == returnType &&
            ParametersMatch(replacementParameters, expectedParameters, offset: 0);
        if (isDirectReplacement)
        {
            return;
        }

        var hasOriginalCall =
            replacementInvoke.ReturnType == returnType &&
            replacementParameters.Length == expectedParameters.Length + 1 &&
            OriginalDelegateMatches(replacementParameters[0].ParameterType, returnType, expectedParameters) &&
            ParametersMatch(replacementParameters, expectedParameters, offset: 1);
        if (hasOriginalCall)
        {
            return;
        }

        var directSignature = FormatSignature(returnType, expectedParameters);
        var actualParameters = GetParameterTypes(replacementParameters);
        var actualSignature = FormatSignature(replacementInvoke.ReturnType, actualParameters);
        throw new ArgumentException(
            $"Replacement delegate '{FormatType(replacement.GetType())}' has signature '{actualSignature}', " +
            $"but target '{FormatTarget(target)}' requires '{directSignature}', optionally preceded by " +
            "an original-call delegate with that required signature.",
            nameof(replacement));
    }

    private static Type[] GetParameterTypes(ParameterInfo[] parameters)
    {
        var types = new Type[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            types[index] = parameters[index].ParameterType;
        }

        return types;
    }

    private static Type[] GetExpectedParameters(MethodBase target)
    {
        var methodParameters = target.GetParameters();
        var offset = target.IsStatic ? 0 : 1;
        var expected = new Type[methodParameters.Length + offset];

        if (!target.IsStatic)
        {
            var declaringType = target.DeclaringType
                ?? throw new NotSupportedException("Instance members without a declaring type are not supported.");
            if (declaringType.IsValueType && target is ConstructorInfo)
            {
                throw new NotSupportedException("Value-type constructors are not supported yet.");
            }

            expected[0] = declaringType.IsValueType
                ? declaringType.MakeByRefType()
                : declaringType;
        }

        for (var index = 0; index < methodParameters.Length; index++)
        {
            expected[index + offset] = methodParameters[index].ParameterType;
        }

        return expected;
    }

    private static Type GetReturnType(MethodBase target)
    {
        return target is MethodInfo method ? method.ReturnType : typeof(void);
    }

    private static bool OriginalDelegateMatches(Type delegateType, Type returnType, Type[] expectedParameters)
    {
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
        {
            return false;
        }

        var invoke = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
        return invoke is not null &&
            invoke.ReturnType == returnType &&
            ParametersMatch(invoke.GetParameters(), expectedParameters, offset: 0);
    }

    private static MethodInfo GetDelegateInvoke(Type delegateType)
    {
        return delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"'{delegateType.FullName}' is not an invokable delegate type.", nameof(delegateType));
    }

    private static bool ParametersMatch(ParameterInfo[] actual, Type[] expected, int offset)
    {
        if (actual.Length != expected.Length + offset)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (actual[index + offset].ParameterType != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatSignature(Type returnType, Type[] parameters)
    {
        var names = new string[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            names[index] = FormatType(parameters[index]);
        }

        return $"{FormatType(returnType)} ({string.Join(", ", names)})";
    }

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            var elementType = type.GetElementType()
                ?? throw new ArgumentException("By-reference type has no element type.", nameof(type));
            return $"byref {FormatType(elementType)}";
        }

        if (type.IsPointer)
        {
            var elementType = type.GetElementType()
                ?? throw new ArgumentException("Pointer type has no element type.", nameof(type));
            return $"{FormatType(elementType)}*";
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType()
                ?? throw new ArgumentException("Array type has no element type.", nameof(type));
            var commas = new string(',', type.GetArrayRank() - 1);
            return $"{FormatType(elementType)}[{commas}]";
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        var definition = type.GetGenericTypeDefinition();
        var name = (definition.FullName ?? definition.Name).Replace('+', '.');
        var arity = name.IndexOf('`');
        if (arity >= 0)
        {
            name = name.Substring(0, arity);
        }

        var arguments = type.GetGenericArguments();
        var formattedArguments = new string[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            formattedArguments[index] = FormatType(arguments[index]);
        }

        return $"{name}<{string.Join(", ", formattedArguments)}>";
    }

    private static string FormatTarget(MethodBase target)
    {
        var declaringType = target.DeclaringType is null
            ? "<global>"
            : FormatType(target.DeclaringType);
        var methodName = target.Name;
        if (target is MethodInfo method && method.IsGenericMethod)
        {
            var genericArguments = method.GetGenericArguments();
            var formattedArguments = new string[genericArguments.Length];
            for (var index = 0; index < genericArguments.Length; index++)
            {
                formattedArguments[index] = FormatType(genericArguments[index]);
            }

            methodName = $"{methodName}<{string.Join(", ", formattedArguments)}>";
        }

        var parameters = target.GetParameters();
        var parameterTypes = GetParameterTypes(parameters);
        return $"{declaringType}.{methodName}{FormatParameters(parameterTypes)}";
    }

    private static string FormatParameters(Type[] parameters)
    {
        var names = new string[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            names[index] = FormatType(parameters[index]);
        }

        return $"({string.Join(", ", names)})";
    }

    private sealed class RuntimeDetourHandle : IDisposable
    {
        private readonly object _sync = new object();
        private readonly string _targetName;
        private Hook? _hook;

        public RuntimeDetourHandle(Hook hook, string targetName)
        {
            _hook = hook ?? throw new ArgumentNullException(nameof(hook));
            _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_hook is null)
                {
                    return;
                }

                try
                {
                    _hook.Dispose();
                    _hook = null;
                }
                catch (Exception exception)
                {
                    throw new InsiderHookException(
                        $"Could not remove managed detour from '{_targetName}'.",
                        exception);
                }
            }
        }
    }
}

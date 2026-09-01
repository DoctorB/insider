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

        if ((target.CallingConvention & CallingConventions.VarArgs) != 0)
        {
            throw new NotSupportedException("Variable-argument methods are not supported by the Insider hook contract.");
        }

        ValidateSignature(target, replacement);

        return new Hook(target, replacement);
    }

    private static void ValidateSignature(MethodInfo target, Delegate replacement)
    {
        var expectedParameters = GetExpectedParameters(target);
        var replacementInvoke = GetDelegateInvoke(replacement.GetType());
        var replacementParameters = replacementInvoke.GetParameters();

        var isDirectReplacement =
            replacementInvoke.ReturnType == target.ReturnType &&
            ParametersMatch(replacementParameters, expectedParameters, offset: 0);
        if (isDirectReplacement)
        {
            return;
        }

        var hasOriginalCall =
            replacementInvoke.ReturnType == target.ReturnType &&
            replacementParameters.Length == expectedParameters.Length + 1 &&
            OriginalDelegateMatches(replacementParameters[0].ParameterType, target.ReturnType, expectedParameters) &&
            ParametersMatch(replacementParameters, expectedParameters, offset: 1);
        if (hasOriginalCall)
        {
            return;
        }

        var directSignature = FormatSignature(target.ReturnType, expectedParameters);
        throw new ArgumentException(
            $"Replacement delegate must match '{directSignature}', optionally preceded by an original-call delegate with the same signature.",
            nameof(replacement));
    }

    private static Type[] GetExpectedParameters(MethodInfo target)
    {
        var methodParameters = target.GetParameters();
        var offset = target.IsStatic ? 0 : 1;
        var expected = new Type[methodParameters.Length + offset];

        if (!target.IsStatic)
        {
            var declaringType = target.DeclaringType
                ?? throw new NotSupportedException("Instance methods without a declaring type are not supported.");
            if (declaringType.IsValueType)
            {
                throw new NotSupportedException("Instance methods declared on value types are not supported yet.");
            }

            expected[0] = declaringType;
        }

        for (var index = 0; index < methodParameters.Length; index++)
        {
            expected[index + offset] = methodParameters[index].ParameterType;
        }

        return expected;
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
        return type.FullName ?? type.Name;
    }
}

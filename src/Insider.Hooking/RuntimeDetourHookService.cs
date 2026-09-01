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
        throw new ArgumentException(
            $"Replacement delegate must match '{directSignature}', optionally preceded by an original-call delegate with the same signature.",
            nameof(replacement));
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
        return type.FullName ?? type.Name;
    }
}

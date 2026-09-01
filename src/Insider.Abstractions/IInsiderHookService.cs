using System;
using System.Reflection;

namespace Insider;

public interface IInsiderHookService
{
    IDisposable Detour(MethodInfo target, Delegate replacement);
}

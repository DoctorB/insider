using System;

namespace Insider;

public interface IInsiderMainThread
{
    bool IsReady { get; }

    bool IsCurrent { get; }

    void Post(Action callback);
}

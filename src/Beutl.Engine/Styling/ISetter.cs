using System.Reactive;

using Beutl.Animation;
using Beutl.Collections;

namespace Beutl.Styling;

public interface ISetter
{
    CoreProperty Property { get; }

    object? Value { get; }

    IAnimation? Animation { get; }

    event EventHandler? Invalidated;

    ISetterInstance Instance(ICoreObject target);

    IObservable<Unit> GetObservable();
}

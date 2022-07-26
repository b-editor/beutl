using System.Reactive;

using BeUtl.Animation;
using BeUtl.Collections;

namespace BeUtl.Styling;

public interface ISetter : IStylingElement
{
    CoreProperty Property { get; }

    object? Value { get; }

    IAnimation? Animation { get; }

    event EventHandler? Invalidated;

    ISetterInstance Instance(IStyleable target);

    IObservable<Unit> GetObservable();
}

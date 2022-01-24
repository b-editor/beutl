using System.Reactive;

using BeUtl.Animation;
using BeUtl.Collections;

namespace BeUtl.Styling;

public interface ISetter
{
    CoreProperty Property { get; }

    object? Value { get; }

    ICoreReadOnlyList<IAnimation> Animations { get; }

    ISetterInstance Instance(IStyleable target);

    IObservable<Unit> GetObservable();
}

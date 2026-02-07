using System.Numerics;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface ITimelineOptionsProvider
{
    Scene Scene { get; }

    IReactiveProperty<TimelineOptions> Options { get; }

    IObservable<float> Scale { get; }

    IObservable<Vector2> Offset { get; }
}

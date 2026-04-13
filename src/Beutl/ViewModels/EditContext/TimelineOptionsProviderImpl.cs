using System.Numerics;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class TimelineOptionsProviderImpl : ITimelineOptionsProvider, IDisposable
{
    public TimelineOptionsProviderImpl(Scene scene)
    {
        Scene = scene;
        Options = new ReactiveProperty<TimelineOptions>(new TimelineOptions());
        Scale = Options.Select(o => o.Scale);
        Offset = Options.Select(o => o.Offset);
    }

    public Scene Scene { get; }

    public ReactiveProperty<TimelineOptions> Options { get; }

    public IObservable<float> Scale { get; }

    public IObservable<Vector2> Offset { get; }

    IReactiveProperty<TimelineOptions> ITimelineOptionsProvider.Options => Options;

    public void Dispose()
    {
        Options.Dispose();
    }
}

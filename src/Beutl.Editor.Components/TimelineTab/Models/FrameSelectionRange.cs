using Beutl.Editor.Components.Helpers;

using Reactive.Bindings;

namespace Beutl.Editor.Components.TimelineTab.Models;

public sealed class FrameSelectionRange : IDisposable
{
    public FrameSelectionRange(IObservable<float> scale)
    {
        PixelStart = Start.CombineLatest(scale)
            .Select(v => v.First.TimeToPixel(v.Second))
            .ToReactiveProperty();

        PixelLength = Length.CombineLatest(scale)
            .Select(v => v.First.TimeToPixel(v.Second))
            .ToReactiveProperty();
    }

    public ReactivePropertySlim<TimeSpan> Start { get; set; } = new();

    public ReactivePropertySlim<TimeSpan> Length { get; set; } = new();

    public ReactiveProperty<double> PixelStart { get; set; } = new();

    public ReactiveProperty<double> PixelLength { get; set; } = new();

    public void Dispose()
    {
        (Start, Length, PixelStart, PixelLength).DisposeAll();
    }
}

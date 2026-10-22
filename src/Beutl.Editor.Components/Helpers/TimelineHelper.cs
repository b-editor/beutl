using Avalonia;
using Avalonia.Controls;
using Beutl.Configuration;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

public static class TimelineHelper
{
    // The timeline and the graph editor place the scene's start and end bars and size the panel the same way.
    internal static (ReadOnlyReactivePropertySlim<Thickness> StartingBarMargin,
        ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin,
        ReadOnlyReactivePropertySlim<double> PanelWidth) CreateSceneBarMargins(
            Scene scene, IEditorClock clock, IObservable<float> scale, CompositeDisposable disposables)
    {
        ReadOnlyReactivePropertySlim<Thickness> startingBarMargin = scene.StartProperty.GetObservable()
            .CombineLatest(scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<Thickness> endingBarMargin = scene.DurationProperty.GetObservable()
            .CombineLatest(scale, startingBarMargin)
            .Select(item => item.First.TimeToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<double> panelWidth = clock.MaximumTime
            .CombineLatest(
                scene.DurationProperty.GetObservable(),
                scene.StartProperty.GetObservable(),
                clock.CurrentTime)
            .Select(i => new MediaTime(
                Math.Max(
                    Math.Max(i.First.Flicks, i.Second.Flicks + i.Third.Flicks),
                    i.Fourth.Flicks)))
            .CombineLatest(scale)
            .Select(i => i.First.TimeToPixel(i.Second) + 500)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        return (startingBarMargin, endingBarMargin, panelWidth);
    }

    // Keeps the playhead in view while the preview plays, as the auto-scroll setting asks.
    internal static void AutoScrollToPlayhead(
        ScrollViewer scrollViewer, IEditorContext editorContext, IReactiveProperty<TimelineOptions> options,
        MediaTime currentTime)
    {
        var mode = GlobalConfiguration.Instance.EditorConfig.TimelineAutoScrollMode;
        if (mode == TimelineAutoScrollMode.None) return;

        var previewPlayer = editorContext.GetService<IPreviewPlayer>();
        if (previewPlayer == null || !previewPlayer.IsPlaying.Value) return;

        float scale = options.Value.Scale;
        double seekBarPixel = currentTime.TimeToPixel(scale);

        double? newOffsetX = CalculateAutoScrollOffset(
            seekBarPixel, scrollViewer.Viewport.Width, scrollViewer.Offset.X, mode);

        if (newOffsetX is not { } offsetX) return;

        scrollViewer.Offset = new Vector(offsetX, scrollViewer.Offset.Y);
    }

    // The zoom menus pass their levels as invariant string literals, and Beutl leaves CurrentCulture at the OS one.
    internal static bool TryParseZoom(object? commandParameter, out float zoom)
    {
        zoom = 0;
        return commandParameter is string str
            && float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out zoom);
    }

    internal static void ShowBpmGridFlyout(IReactiveProperty<TimelineOptions> options, Control target)
    {
        var bpmGrid = options.Value.BpmGrid;
        var flyout = new BpmGridFlyout
        {
            IsEnabledChecked = bpmGrid.IsEnabled,
            Bpm = (decimal)bpmGrid.Bpm,
            Subdivisions = bpmGrid.Subdivisions,
            OffsetSeconds = (decimal)bpmGrid.Offset.ToSeconds(),
        };
        flyout.OptionsChanged += (_, bpmGridOptions) =>
        {
            options.Value = options.Value with { BpmGrid = bpmGridOptions };
        };

        flyout.ShowAt(target, true);
    }

    public static double? CalculateAutoScrollOffset(
        double seekBarPixel,
        double viewportWidth,
        double currentOffsetX,
        TimelineAutoScrollMode mode)
    {
        double newOffsetX;

        if (mode == TimelineAutoScrollMode.AlwaysFollow)
        {
            newOffsetX = seekBarPixel - (viewportWidth / 2);
        }
        else // PageScroll
        {
            if (seekBarPixel >= currentOffsetX && seekBarPixel <= currentOffsetX + viewportWidth)
                return null;

            if (seekBarPixel > currentOffsetX + viewportWidth)
            {
                newOffsetX = seekBarPixel - (viewportWidth * 0.1);
            }
            else
            {
                newOffsetX = seekBarPixel - (viewportWidth * 0.9);
            }
        }

        return Math.Max(0, newOffsetX);
    }

    public enum MouseFlags
    {
        Free,
        SeekBarPressed,
        RangeSelectionPressed,
        EndingBarMarkerPressed,
        StartingBarMarkerPressed,
        MarkerPressed
    }

    private const int MarkerHeight = 18;
    private const int MarkerWidth = 4;

    public static bool IsPointInTimelineScaleMarker(double x, double y, double startingBarX, double endingBarX)
    {
        return IsPointInTimelineScaleStartingMarker(x, y, startingBarX) ||
               IsPointInTimelineScaleEndingMarker(x, y, endingBarX);
    }

    public static bool IsPointInTimelineScaleStartingMarker(double x, double y, double startingBarX)
    {
        var startRect = new Rect(startingBarX, 0, MarkerWidth, MarkerHeight);
        return startRect.Contains(new Point(x, y));
    }

    public static bool IsPointInTimelineScaleEndingMarker(double x, double y, double endingBarX)
    {
        var endRect = new Rect(endingBarX - MarkerWidth, 0, MarkerWidth, MarkerHeight);
        return endRect.Contains(new Point(x, y));
    }
}

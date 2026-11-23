using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

public static class TimelineHelper
{
    internal static Vector GetScrollOffsetDelta(PointerWheelEventArgs e, bool gestureAxes, EditorConfig configuration)
    {
        Vector delta = e.Delta;
        if (!gestureAxes && OperatingSystem.IsWindows() && e.KeyModifiers == KeyModifiers.Shift)
            delta = new Vector(delta.Y, delta.X);

        // Touchpad gestures keep their native axes; wheels follow the editor preference.
        if (!gestureAxes && !configuration.SwapTimelineScrollDirection.Value)
            delta = new Vector(delta.Y, delta.X);
        return -delta * 50;
    }

    // How the timeline's and the graph editor's rulers write times: as the player writes them.
    internal static ReadOnlyReactivePropertySlim<TimeDisplayFormat> ObserveTimeDisplayFormat(EditorConfig configuration, CompositeDisposable disposables)
        => configuration.TimeDisplayFormat.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

    // The timeline and the graph editor place the scene's start and end bars and size the panel the same way.
    internal static (ReadOnlyReactivePropertySlim<Thickness> StartingBarMargin,
        ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin,
        ReadOnlyReactivePropertySlim<double> PanelWidth) CreateSceneBarMargins(
            Scene scene, IEditorClock clock, IObservable<float> scale, CompositeDisposable disposables)
    {
        ReadOnlyReactivePropertySlim<Thickness> startingBarMargin = scene.Start.GetObservable()
            .CombineLatest(scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<Thickness> endingBarMargin = scene.Duration.GetObservable()
            .CombineLatest(scale, startingBarMargin)
            .Select(item => item.First.TimeToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<double> panelWidth = clock.MaximumTime
            .CombineLatest(
                scene.Duration.GetObservable(),
                scene.Start.GetObservable(),
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
        var mode = editorContext.GetRequiredService<GlobalConfiguration>().EditorConfig.TimelineAutoScrollMode.Value;
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

    // Keeps the content under pointerX in place while the scale changes.
    internal static float ZoomOffsetX(double pointerX, float offsetX, float oldScale, float newScale)
    {
        double deltaLeft = pointerX - offsetX;
        return (float)((pointerX / oldScale * newScale) - deltaLeft);
    }

    // The scale and offset at which the range fills a view of the given width, less the margin at each side. The view
    // cannot scroll left of time zero, so a range that would start within the margin of zero starts at the left edge
    // and keeps the margin at its end only. A range too short to fill the view at the largest scale is centered, and
    // one too long for the smallest starts at the margin.
    internal static (float Scale, double OffsetX) FitRange(TimeRange range, double viewportWidth, double margin)
    {
        // The range's ends at scale 1.
        double start = range.Start.TimeToPixel(1);
        double end = range.End.TimeToPixel(1);
        double scale = end > start ? Math.Max(1, viewportWidth - 2 * margin) / (end - start) : TimelineOptions.MaxScale;
        double offset = start * scale - margin;
        if (offset < 0 && end > start)
        {
            scale = Math.Max(1, viewportWidth - margin) / end;
            offset = 0;
        }

        if (scale >= TimelineOptions.MaxScale)
        {
            return (TimelineOptions.MaxScale,
                Math.Max(0, (start + end) / 2 * TimelineOptions.MaxScale - viewportWidth / 2));
        }

        if (scale <= TimelineOptions.MinScale)
        {
            return (TimelineOptions.MinScale, Math.Max(0, start * TimelineOptions.MinScale - margin));
        }

        return ((float)scale, offset);
    }

    // Moves a view from one scale and offset to another about the point that both views show at the same x, so that
    // every step zooms about that point, as the wheel zooms about the pointer. Between two views at one scale, which
    // have no such point, it scrolls.
    internal sealed class ZoomMotion(float fromScale, Vector fromOffset, float toScale, Vector toOffset)
    {
        public (float Scale, Vector Offset) At(double progress)
        {
            double y = fromOffset.Y + (toOffset.Y - fromOffset.Y) * progress;
            if (Math.Abs(toScale - fromScale) <= fromScale * 1e-4)
            {
                return ((float)(fromScale + (toScale - fromScale) * progress),
                    new Vector(fromOffset.X + (toOffset.X - fromOffset.X) * progress, y));
            }

            double scale = fromScale * Math.Pow((double)toScale / fromScale, progress);
            // The point's place at scale 1, from x = place * fromScale - fromOffset.X = place * toScale - toOffset.X.
            double place = (toOffset.X - fromOffset.X) / ((double)toScale - fromScale);
            return ((float)scale, new Vector(place * scale - (place * fromScale - fromOffset.X), y));
        }
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

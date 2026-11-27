using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Views;

public sealed partial class TimelineScale : TimelineBarsControl
{
    private const int FramesPerTick = 30;
    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private ImmutablePen? _pen;
    private static readonly Geometry _rangeMarker;
    private static readonly Geometry _pointMarker;
    private INotifyCollectionChanged? _trackedMarkers;
    private FrameSelectionRange? _frameSelection;
    private readonly Dictionary<SceneMarker, IDisposable> _markerSubscriptions = new();
    // The ruler is drawn again on every frame of playback, since it draws the seek bar, while its texts stay the same.
    private readonly TextLayoutCache _tickLabels = new(13);
    private readonly TextLayoutCache _markerLabels = new(11);

    static TimelineScale()
    {
        _rangeMarker = Geometry.Parse("M 0,0 L 4,0 L 4,8 L 0,16 Z");
        _pointMarker = Geometry.Parse("M -4,0 L 4,0 L 4,8 L 0,16 L -4,8 Z");
        AffectsRender<TimelineScale>(
            ScaleProperty,
            OffsetProperty,
            BufferStartProperty,
            BufferEndProperty,
            CacheBlocksProperty,
            HoveredCacheBlockProperty,
            ScaleBrushProperty,
            CacheBlockBrushProperty,
            LockedCacheBlockBrushProperty,
            BufferBrushProperty,
            MarkersProperty,
            MarkerLabelBackgroundProperty,
            MarkerLabelForegroundProperty,
            FrameRateProperty,
            TimeDisplayFormatProperty);
    }

    public TimelineScale()
    {
        ClipToBounds = true;
    }

    [GeneratedDirectProperty(UnsetValue = 1)]
    public partial float Scale { get; set; } = 1;

    [GeneratedDirectProperty]
    public partial Vector Offset { get; set; }

    [GeneratedStyledProperty]
    public partial double BufferStart { get; set; }

    [GeneratedStyledProperty]
    public partial double BufferEnd { get; set; }

    [GeneratedStyledProperty]
    public partial CacheBlock[]? CacheBlocks { get; set; }

    [GeneratedStyledProperty]
    public partial CacheBlock? HoveredCacheBlock { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ScaleBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? CacheBlockBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? LockedCacheBlockBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? BufferBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IEnumerable? Markers { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? MarkerLabelBackground { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? MarkerLabelForeground { get; set; }

    /// <summary>Gets or sets the rate that the ruler's timecodes and frame numbers count frames at. It must be positive.</summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(PlayerSeekBar))]
    public partial Rational FrameRate { get; set; }

    /// <summary>Gets or sets how the ruler writes the times of its ticks.</summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(PlayerSeekBar))]
    public partial TimeDisplayFormat TimeDisplayFormat { get; set; }

    // The layouts made for the ruler's texts so far; tests check that a render that only moves the seek bar makes none.
    internal int LabelLayoutsMade => _tickLabels.Made + _markerLabels.Made;

    internal void SetFrameSelection(FrameSelectionRange? selection)
    {
        _frameSelection = selection;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleBrushProperty)
        {
            _pen = new ImmutablePen(ScaleBrush?.ToImmutable(), 1);
            _tickLabels.Clear();
            _markerLabels.Clear();
        }
        else if (change.Property == MarkerLabelForegroundProperty)
        {
            _markerLabels.Clear();
        }
        else if (change.Property == MarkersProperty)
        {
            DetachMarkers();
            AttachMarkers(change.NewValue as IEnumerable);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tickLabels.Clear();
        _markerLabels.Clear();
    }

    private void AttachMarkers(IEnumerable? markers)
    {
        if (markers is INotifyCollectionChanged ncc)
        {
            _trackedMarkers = ncc;
            ncc.CollectionChanged += OnMarkersCollectionChanged;
        }

        if (markers != null)
        {
            foreach (object? item in markers)
            {
                if (item is SceneMarker marker)
                {
                    SubscribeMarker(marker);
                }
            }
        }
    }

    private void DetachMarkers()
    {
        if (_trackedMarkers != null)
        {
            _trackedMarkers.CollectionChanged -= OnMarkersCollectionChanged;
            _trackedMarkers = null;
        }

        foreach (var sub in _markerSubscriptions.Values)
        {
            sub.Dispose();
        }

        _markerSubscriptions.Clear();
    }

    private void OnMarkersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
            {
                if (item is SceneMarker marker
                    && _markerSubscriptions.Remove(marker, out IDisposable? sub))
                {
                    sub.Dispose();
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
            {
                if (item is SceneMarker marker)
                {
                    SubscribeMarker(marker);
                }
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var sub in _markerSubscriptions.Values)
            {
                sub.Dispose();
            }

            _markerSubscriptions.Clear();
        }

        InvalidateVisual();
    }

    private void SubscribeMarker(SceneMarker marker)
    {
        if (_markerSubscriptions.ContainsKey(marker)) return;

        // The values DrawMarkers draws.
        _markerSubscriptions[marker] = new CompositeDisposable(
            marker.Time.GetObservable().Subscribe(_ => InvalidateVisual()),
            marker.Color.GetObservable().Subscribe(_ => InvalidateVisual()),
            marker.Name.GetObservable().Subscribe(_ => InvalidateVisual()));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _pen ??= new ImmutablePen(ScaleBrush?.ToImmutable(), 1);

        double width = Bounds.Width;
        double height = Bounds.Height;
        var viewport = new Rect(new Point(Offset.X, 0), new Size(width, height));

        double inc = FrameNumberHelper.SecondWidth;
        double l = viewport.Width + viewport.X;

        double originX = Math.Floor(viewport.X / inc) * inc;
        using (context.PushTransform(Matrix.CreateTranslation(-viewport.X, 0)))
        {
            context.FillRectangle(Brushes.Transparent, viewport);
            if (_frameSelection is { } selection)
            {
                double left = selection.Start.TimeToPixel(Scale);
                double right = selection.End.TimeToPixel(Scale);
                var bounds = new Rect(left, 0, right - left, height);
                context.DrawRectangle(TimelineSharedObject.SelectionFillBrush, TimelineSharedObject.SelectionPen, bounds);
            }
            DrawTimeTicks(context, _pen, viewport, height, originX, l);
            DrawBufferAndCacheBlocks(context, originX, l);
            DrawBars(context, height);
            DrawRangeMarkers(context);
            DrawMarkers(context, viewport);
        }

        _tickLabels.EndRender();
        _markerLabels.EndRender();
    }

    private void DrawTimeTicks(DrawingContext context, IPen pen, Rect viewport, double height, double originX, double viewportRight)
    {
        const int top = 16;

        double recentPix = 0d;
        double inc = FrameNumberHelper.SecondWidth;
        double wf = FrameNumberHelper.SecondWidth / FramesPerTick;
        for (double x = originX; x < viewportRight; x += inc)
        {
            var time = x.PixelToTime(Scale);

            if (viewport.Contains(new Point(x, height)))
            {
                context.DrawLine(pen, new(x, 5), new(x, height));
            }

            TextLayout text = _tickLabels.Get(TimeDisplayFormatter.Format(time, FrameRate, TimeDisplayFormat), ScaleBrush);
            var textbounds = new Rect(x + 8, 0, text.Width, text.Height);

            if (viewport.Intersects(textbounds) && (recentPix == 0d || (x + 8) > recentPix))
            {
                recentPix = textbounds.Right;
                text.Draw(context, new(x + 8, 0));
            }

            double ll = x + inc;
            for (double xx = x + wf; xx < ll; xx += wf)
            {
                if (!viewport.Contains(new Point(xx, height))) continue;

                context.DrawLine(pen, new(xx, top), new(xx, height));
            }
        }
    }

    private void DrawBufferAndCacheBlocks(DrawingContext context, double originX, double viewportRight)
    {
        if (BufferEnd != BufferStart)
        {
            context.DrawRectangle(
                BufferBrush, null,
                new RoundedRect(new Rect(BufferStart, Height - 4, BufferEnd - BufferStart, 4)));
        }

        if (CacheBlocks != null)
        {
            MediaTime left = originX.PixelToTime(Scale);
            MediaTime right = viewportRight.PixelToTime(Scale);

            foreach (CacheBlock item in CacheBlocks)
            {
                MediaTime end = item.Start + item.Length;
                if (end < left || item.Start > right)
                {
                    continue;
                }

                context.DrawRectangle(
                    item.IsLocked ? LockedCacheBlockBrush : CacheBlockBrush, null,
                    new RoundedRect(new Rect(item.Start.TimeToPixel(Scale), Height - 4, item.Length.TimeToPixel(Scale),
                        4)));
            }
        }

        if (HoveredCacheBlock is { } hover)
        {
            context.DrawRectangle(
                hover.IsLocked ? LockedCacheBlockBrush : CacheBlockBrush, null,
                new RoundedRect(new Rect(hover.Start.TimeToPixel(Scale), Height - 6, hover.Length.TimeToPixel(Scale), 6)));
        }
    }

    private void DrawRangeMarkers(DrawingContext context)
    {
        // 開始・終了バーのドラッグ用マーカーを描画
        if (EndingBarBrush?.ToImmutable() is { } brush)
        {
            using (context.PushTransform(Matrix.CreateTranslation(EndingBarMargin.Left, 0)))
            using (context.PushTransform(Matrix.CreateScale(-1, 1)))
            {
                context.DrawGeometry(brush, null, _rangeMarker);
            }
        }

        if (StartingBarBrush?.ToImmutable() is { } brush2)
        {
            using (context.PushTransform(Matrix.CreateTranslation(StartingBarMargin.Left, 0)))
            {
                context.DrawGeometry(brush2, null, _rangeMarker);
            }
        }
    }

    private void DrawMarkers(DrawingContext context, Rect viewport)
    {
        if (Markers == null) return;

        foreach (object? obj in Markers)
        {
            if (obj is not SceneMarker marker) continue;

            double x = marker.Time.Value.TimeToPixel(Scale);
            if (x < viewport.X - 8 || x > viewport.X + viewport.Width + 8) continue;

            var fillBrush = new ImmutableSolidColorBrush(marker.Color.Value.ToAvaColor());
            using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
            {
                context.DrawGeometry(fillBrush, null, _pointMarker);
            }

            string label = marker.Name.Value;
            if (!string.IsNullOrEmpty(label))
            {
                TextLayout text = _markerLabels.Get(label, MarkerLabelForeground ?? ScaleBrush);
                var origin = new Point(x + 8, 1);
                if (MarkerLabelBackground is { } bg)
                {
                    const double padX = 4;
                    const double padY = 1;
                    var bgRect = new Rect(origin.X - padX, origin.Y - padY, text.Width + padX * 2, text.Height + padY * 2);
                    context.DrawRectangle(bg, null, new RoundedRect(bgRect, 3));
                }
                text.Draw(context, origin);
            }
        }
    }

    public SceneMarker? HitTestMarker(double x, double y)
    {
        if (Markers == null) return null;
        if (y < 0 || y > 16) return null;

        SceneMarker? best = null;
        double bestDist = double.PositiveInfinity;
        foreach (object? obj in Markers)
        {
            if (obj is not SceneMarker marker) continue;
            double mx = marker.Time.Value.TimeToPixel(Scale);
            double dist = Math.Abs(mx - x);
            if (dist <= 6 && dist < bestDist)
            {
                bestDist = dist;
                best = marker;
            }
        }

        return best;
    }

    // Keeps the layout of each text a render uses for the next render, which lets go of those it does not use. All the
    // texts take one brush, so a new brush clears it.
    private sealed class TextLayoutCache(double fontSize)
    {
        private Dictionary<string, TextLayout> _kept = [];
        private Dictionary<string, TextLayout> _used = [];

        public int Made { get; private set; }

        public TextLayout Get(string text, IBrush? brush)
        {
            if (_used.TryGetValue(text, out TextLayout? layout))
                return layout;

            if (!_kept.Remove(text, out layout))
            {
                layout = new TextLayout(text, s_typeface, fontSize, brush);
                Made++;
            }

            _used.Add(text, layout);
            return layout;
        }

        public void EndRender()
        {
            DisposeAll(_kept);
            (_kept, _used) = (_used, _kept);
        }

        public void Clear()
        {
            DisposeAll(_kept);
            DisposeAll(_used);
        }

        private static void DisposeAll(Dictionary<string, TextLayout> layouts)
        {
            foreach (TextLayout layout in layouts.Values)
                layout.Dispose();

            layouts.Clear();
        }
    }
}

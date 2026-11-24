using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Beutl.Engine;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class WaveformControl : Control
{
    private static readonly IImmutableBrush s_highlightBrush = new ImmutableSolidColorBrush(Color.FromArgb(90, 245, 150, 45));
    private readonly Dictionary<int, WaveformChunk> _chunks = new();
    private ScrollViewer? _scrollViewer;
    private IReadOnlyList<(double Start, double End)> _highlights = [];
    private int _lastNotifiedStart = -1;
    private int _lastNotifiedEnd = -1;

    public event Action<int, int>? VisibleRangeChanged;

    public void SetHighlights(IReadOnlyList<(double Start, double End)> ranges)
    {
        _highlights = ranges;
        InvalidateVisual();
    }

    public void SetChunks(IReadOnlyList<WaveformChunk> chunks)
    {
        ChunkCount = chunks.Count;
        _chunks.Clear();
        foreach (var chunk in chunks) _chunks[chunk.Index] = chunk;
        InvalidateVisual();
    }

    public static readonly StyledProperty<IBrush?> WaveformBrushProperty =
        AvaloniaProperty.Register<WaveformControl, IBrush?>(nameof(WaveformBrush), Brushes.White);

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(
            ChunkCountProperty,
            WaveformBrushProperty,
            ContentWidthProperty,
            ContentOffsetXProperty,
            IsPreviewActiveProperty);
    }

    [GeneratedStyledProperty]
    public partial int ChunkCount { get; set; }

    [GeneratedStyledProperty(DefaultValue = double.NaN)]
    public partial double ContentWidth { get; set; }

    [GeneratedStyledProperty]
    public partial double ContentOffsetX { get; set; }

    [GeneratedStyledProperty]
    public partial bool IsPreviewActive { get; set; }

    private bool HasCustomLayout => IsPreviewActive || !double.IsNaN(ContentWidth) || ContentOffsetX != 0;

    public IBrush? WaveformBrush
    {
        get => GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
    }

    public void SetChunk(WaveformChunk chunk)
    {
        _chunks[chunk.Index] = chunk;

        InvalidateVisual();
    }

    public void ClearChunks()
    {
        _chunks.Clear();
        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;

        InvalidateVisual();
    }

    public void PruneOutOfRangeChunks()
    {
        int count = ChunkCount;
        List<int>? toRemove = null;
        foreach (int key in _chunks.Keys)
        {
            if (key < 0 || key >= count)
            {
                toRemove ??= [];
                toRemove.Add(key);
            }
        }

        if (toRemove != null)
        {
            foreach (int key in toRemove)
                _chunks.Remove(key);
        }

        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;
        InvalidateVisual();
    }

    internal (int Start, int End) GetVisibleRange()
        => TimelineSlotRange.Compute(
            this,
            _scrollViewer,
            ChunkCount,
            buffer: 16,
            ContentWidth,
            ContentOffsetX,
            allowOutOfRange: false);

    public List<int> GetMissingIndices(int start, int end)
    {
        var missing = new List<int>();
        for (int i = start; i <= end; i++)
        {
            if (!_chunks.ContainsKey(i))
                missing.Add(i);
        }
        return missing;
    }

    private void NotifyVisibleRangeIfChanged()
    {
        if (!IsPreviewActive)
            return;

        var (start, end) = TimelineSlotRange.Compute(
            this,
            _scrollViewer,
            ChunkCount,
            buffer: 16,
            ContentWidth,
            ContentOffsetX,
            allowOutOfRange: true);
        if (start != _lastNotifiedStart || end != _lastNotifiedEnd)
        {
            _lastNotifiedStart = start;
            _lastNotifiedEnd = end;
            VisibleRangeChanged?.Invoke(start, end);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChunkCountProperty)
        {
            ClearChunks();
        }
        else if (change.Property == IsPreviewActiveProperty)
        {
            _lastNotifiedStart = -1;
            _lastNotifiedEnd = -1;
            NotifyVisibleRangeIfChanged();
        }
        else if (change.Property == ContentOffsetXProperty
            || change.Property == ContentWidthProperty
            || change.Property == BoundsProperty)
        {
            NotifyVisibleRangeIfChanged();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        double width = bounds.Width;
        double height = bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        int count = ChunkCount;
        if (count <= 0)
            return;

        var brush = WaveformBrush;
        if (brush == null)
            return;

        double contentWidth = !double.IsNaN(ContentWidth) && ContentWidth > 0 ? ContentWidth : width;
        double slotWidth = contentWidth / count;
        if (slotWidth <= 0)
            return;

        double contentOffsetX = ContentOffsetX;
        double centerY = height / 2;

        foreach (var range in _highlights)
        {
            double left = Math.Clamp(range.Start, 0, 1) * width;
            double right = Math.Clamp(range.End, 0, 1) * width;
            if (right > left)
                context.FillRectangle(s_highlightBrush, new Rect(left, 0, right - left, height));
        }

        bool customLayout = HasCustomLayout;
        var (startIndex, endIndex) = TimelineSlotRange.Compute(
            this,
            _scrollViewer,
            count,
            buffer: 1,
            contentWidth,
            contentOffsetX,
            allowOutOfRange: customLayout);

        if (customLayout)
        {
            using (context.PushClip(new Rect(0, 0, width, height)))
            {
                DrawChunks(context, brush, startIndex, endIndex, slotWidth, contentOffsetX, centerY);
            }
        }
        else
        {
            DrawChunks(context, brush, startIndex, endIndex, slotWidth, contentOffsetX, centerY);
        }
    }

    private void DrawChunks(
        DrawingContext context,
        IBrush brush,
        int startIndex,
        int endIndex,
        double slotWidth,
        double contentOffsetX,
        double centerY)
    {
        foreach (var kvp in _chunks)
        {
            int i = kvp.Key;
            var chunk = kvp.Value;

            if (i < startIndex || i > endIndex)
                continue;

            double slotX = contentOffsetX + i * slotWidth;

            float minVal = Math.Clamp(chunk.MinValue, -1f, 1f);
            float maxVal = Math.Clamp(chunk.MaxValue, -1f, 1f);

            double topY = centerY - (maxVal * centerY);
            double bottomY = centerY - (minVal * centerY);
            double barHeight = Math.Max(1, bottomY - topY);

            var rect = new Rect(slotX, topY, Math.Max(1, slotWidth - 0.5), barHeight);
            context.FillRectangle(brush, rect);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        _scrollViewer?.PropertyChanged += OnScrollViewerPropertyChanged;
        NotifyVisibleRangeIfChanged();
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            InvalidateVisual();
            NotifyVisibleRangeIfChanged();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearChunks();
        _scrollViewer?.PropertyChanged -= OnScrollViewerPropertyChanged;
        _scrollViewer = null;
    }
}

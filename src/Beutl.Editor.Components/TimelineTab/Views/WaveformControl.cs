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
    private readonly TimelineMediaSlots<WaveformChunk> _chunks = new();
    private ScrollViewer? _scrollViewer;
    private IReadOnlyList<(double Start, double End)> _highlights = [];
    private int _lastNotifiedStart = -1;
    private int _lastNotifiedEnd = -1;
    private (int Start, int End, double SlotWidth, double Offset, double Center) _geometryLayout;
    private StreamGeometry? _geometry;

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
        foreach (var chunk in chunks)
            _chunks.Set(chunk.Index, chunk);
        InvalidateChunks();
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
        _chunks.Set(chunk.Index, chunk, IsPreviewActive);

        InvalidateChunks();
    }

    internal void SetChunkBatch(IReadOnlyList<WaveformChunk> chunks)
    {
        foreach (var chunk in chunks)
            _chunks.Set(chunk.Index, chunk, IsPreviewActive);
        InvalidateChunks();
    }

    public void ClearChunks()
    {
        _chunks.Clear();
        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;

        InvalidateChunks();
    }

    internal void ClearPreview()
    {
        _chunks.ClearPreview();
        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;
        InvalidateChunks();
    }

    internal void CommitPreview() => _chunks.CommitPreview();

    private void InvalidateChunks()
    {
        _geometry = null;
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
            allowOutOfRange: IsPreviewActive);

    internal (int Start, int End) GetVisibleRange(int count, double width)
        => TimelineSlotRange.Compute(this, _scrollViewer, count, buffer: 16, layoutWidth: width);

    public List<int> GetMissingIndices(int start, int end) => _chunks.GetMissingIndices(start, end);

    internal bool TryGetChunk(int index, out WaveformChunk chunk) => _chunks.TryGetValue(index, out chunk);

    private void NotifyVisibleRangeIfChanged()
    {
        var (start, end) = GetVisibleRange();
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
            NotifyVisibleRangeIfChanged();
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
        var layout = (startIndex, endIndex, slotWidth, contentOffsetX, centerY);
        if (_geometry == null || _geometryLayout != layout)
        {
            _geometryLayout = layout;
            _geometry = new StreamGeometry();
            using var figures = _geometry.Open();
            figures.SetFillRule(FillRule.NonZero);
            double barWidth = Math.Max(1, slotWidth - 0.5);
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (!TryGetChunk(i, out var chunk) || float.IsNaN(chunk.MinValue) || float.IsNaN(chunk.MaxValue))
                    continue;

                double left = contentOffsetX + i * slotWidth;
                double right = left + barWidth;
                double top = centerY - Math.Clamp(chunk.MaxValue, -1f, 1f) * centerY;
                double bottom = Math.Max(top + 1, centerY - Math.Clamp(chunk.MinValue, -1f, 1f) * centerY);

                figures.BeginFigure(new Point(left, top), isFilled: true);
                figures.LineTo(new Point(right, top));
                figures.LineTo(new Point(right, bottom));
                figures.LineTo(new Point(left, bottom));
                figures.EndFigure(isClosed: true);
            }
        }
        context.DrawGeometry(brush, null, _geometry);
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Beutl.Engine;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed class WaveformControl : Control
{
    public static readonly StyledProperty<int> ChunkCountProperty =
        AvaloniaProperty.Register<WaveformControl, int>(nameof(ChunkCount));

    public static readonly StyledProperty<IBrush?> WaveformBrushProperty =
        AvaloniaProperty.Register<WaveformControl, IBrush?>(nameof(WaveformBrush), Brushes.White);

    private static readonly IImmutableBrush s_highlightBrush = new ImmutableSolidColorBrush(Color.FromArgb(90, 245, 150, 45));
    private readonly Dictionary<int, WaveformChunk> _chunks = new();
    private ScrollViewer? _scrollViewer;
    private IReadOnlyList<(double Start, double End)> _highlights = [];

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

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(ChunkCountProperty, WaveformBrushProperty);
    }

    public int ChunkCount
    {
        get => GetValue(ChunkCountProperty);
        set => SetValue(ChunkCountProperty, value);
    }

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

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChunkCountProperty)
        {
            ClearChunks();
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

        double slotWidth = width / count;
        double centerY = height / 2;

        foreach (var range in _highlights)
        {
            double left = Math.Clamp(range.Start, 0, 1) * width;
            double right = Math.Clamp(range.End, 0, 1) * width;
            if (right > left)
                context.FillRectangle(s_highlightBrush, new Rect(left, 0, right - left, height));
        }

        var (startIndex, endIndex) = TimelineSlotRange.Compute(this, _scrollViewer, count, buffer: 1);

        foreach (var kvp in _chunks)
        {
            int i = kvp.Key;
            var chunk = kvp.Value;

            if (i < startIndex || i > endIndex)
                continue;

            double slotX = i * slotWidth;

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
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            InvalidateVisual();
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

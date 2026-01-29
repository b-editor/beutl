using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Operation;

namespace Beutl.Views;

public sealed class WaveformControl : Control
{
    public static readonly StyledProperty<int> ChunkCountProperty =
        AvaloniaProperty.Register<WaveformControl, int>(nameof(ChunkCount));

    public static readonly StyledProperty<IBrush?> WaveformBrushProperty =
        AvaloniaProperty.Register<WaveformControl, IBrush?>(nameof(WaveformBrush), Brushes.White);

    private readonly Dictionary<int, WaveformChunk> _chunks = new();
    private readonly Lock _lock = new();
    private ScrollViewer? _scrollViewer;

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
        lock (_lock)
        {
            _chunks[chunk.Index] = chunk;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public void ClearChunks()
    {
        lock (_lock)
        {
            _chunks.Clear();
        }

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

        lock (_lock)
        {
            // 可視範囲を計算
            int startIndex = 0;
            int endIndex = count - 1;

            if (_scrollViewer != null)
            {
                var topLeft = this.TranslatePoint(Bounds.TopLeft, _scrollViewer);
                var bottomRight = this.TranslatePoint(Bounds.BottomRight, _scrollViewer);
                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    var translatedBounds = new Rect(topLeft.Value, bottomRight.Value);
                    double minX = Math.Max(0, -translatedBounds.Left);
                    double maxX = _scrollViewer.Viewport.Width - translatedBounds.Left;

                    // 少し余裕を持たせる(1スロット分)
                    startIndex = Math.Max(0, (int)(minX / slotWidth) - 1);
                    endIndex = Math.Min(count - 1, (int)Math.Ceiling(maxX / slotWidth) + 1);

                    // Console.WriteLine($"minX: {minX}, maxX: {maxX}, startIndex: {startIndex}, endIndex: {endIndex}");
                }
            }

            foreach (var kvp in _chunks)
            {
                int i = kvp.Key;
                var chunk = kvp.Value;

                // 範囲外はスキップ
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

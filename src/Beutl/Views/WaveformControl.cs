using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
            foreach (var kvp in _chunks)
            {
                int i = kvp.Key;
                var chunk = kvp.Value;

                if (i < 0 || i >= count)
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearChunks();
    }
}

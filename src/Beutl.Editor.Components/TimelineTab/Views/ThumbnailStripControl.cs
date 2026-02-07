using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Stretch = Avalonia.Media.Stretch;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed class ThumbnailStripControl : Control
{
    public static readonly StyledProperty<int> ThumbnailCountProperty =
        AvaloniaProperty.Register<ThumbnailStripControl, int>(nameof(ThumbnailCount));

    private readonly Dictionary<int, Bitmap?> _thumbnails = new();
    private readonly Lock _lock = new();
    private ScrollViewer? _scrollViewer;

    static ThumbnailStripControl()
    {
        AffectsRender<ThumbnailStripControl>(ThumbnailCountProperty);
    }

    public int ThumbnailCount
    {
        get => GetValue(ThumbnailCountProperty);
        set => SetValue(ThumbnailCountProperty, value);
    }

    public void SetThumbnail(int index, Bitmap? thumbnail)
    {
        lock (_lock)
        {
            if (_thumbnails.TryGetValue(index, out var old))
            {
                old?.Dispose();
            }

            _thumbnails[index] = thumbnail;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public void ClearThumbnails()
    {
        lock (_lock)
        {
            foreach (var kvp in _thumbnails)
            {
                kvp.Value?.Dispose();
            }

            _thumbnails.Clear();
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThumbnailCountProperty)
        {
            ClearThumbnails();
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

        int count = ThumbnailCount;
        if (count <= 0)
            return;

        double slotWidth = width / count;
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

            foreach (var kvp in _thumbnails)
            {
                int i = kvp.Key;
                var img = kvp.Value;

                // 範囲外はスキップ
                if (img == null || i < startIndex || i > endIndex)
                    continue;

                double slotX = i * slotWidth;

                // 背景が見えてしまうので微調整
                var dstSize = new Size(slotWidth + 1, height);
                var srcSize = img.Size;
                var viewPort = new Rect(dstSize);

                Vector scale = Stretch.UniformToFill.CalculateScaling(dstSize, srcSize);
                Size scaledSize = srcSize * scale;
                Rect dstRect = viewPort
                    .CenterRect(new Rect(scaledSize))
                    .Intersect(viewPort);
                Rect srcRect = new Rect(srcSize)
                    .CenterRect(new Rect(dstRect.Size / scale));

                dstRect = dstRect.Translate(new(slotX, 0));

                context.DrawImage(img, srcRect, dstRect);
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
        ClearThumbnails();
        _scrollViewer?.PropertyChanged -= OnScrollViewerPropertyChanged;
        _scrollViewer = null;
    }
}

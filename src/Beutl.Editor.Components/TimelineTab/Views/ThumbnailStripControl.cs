using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Stretch = Avalonia.Media.Stretch;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ThumbnailStripControl : Control
{
    private readonly TimelineMediaSlots<WriteableBitmap?> _thumbnails = new(static bitmap => bitmap?.Dispose());
    private ScrollViewer? _scrollViewer;
    private int _lastNotifiedStart = -1;
    private int _lastNotifiedEnd = -1;

    public event Action<int, int>? VisibleRangeChanged;

    static ThumbnailStripControl()
    {
        AffectsRender<ThumbnailStripControl>(
            ThumbnailCountProperty,
            ContentWidthProperty,
            ContentOffsetXProperty,
            IsPreviewActiveProperty);
    }

    [GeneratedStyledProperty]
    public partial int ThumbnailCount { get; set; }

    [GeneratedStyledProperty(DefaultValue = double.NaN)]
    public partial double ContentWidth { get; set; }

    [GeneratedStyledProperty]
    public partial double ContentOffsetX { get; set; }

    [GeneratedStyledProperty]
    public partial bool IsPreviewActive { get; set; }

    private bool HasCustomLayout => IsPreviewActive || !double.IsNaN(ContentWidth) || ContentOffsetX != 0;

    public void SetThumbnail(int index, WriteableBitmap? thumbnail)
    {
        _thumbnails.Set(index, thumbnail, IsPreviewActive);

        InvalidateVisual();
    }

    public void ClearThumbnails()
    {
        _thumbnails.Clear();
        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;

        InvalidateVisual();
    }

    internal void ClearPreview()
    {
        _thumbnails.ClearPreview();
        _lastNotifiedStart = -1;
        _lastNotifiedEnd = -1;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThumbnailCountProperty)
        {
            ClearThumbnails();
            NotifyVisibleRangeIfChanged();
        }
        else if (change.Property == ContentOffsetXProperty
            || change.Property == ContentWidthProperty
            || change.Property == IsPreviewActiveProperty
            || change.Property == BoundsProperty)
        {
            NotifyVisibleRangeIfChanged();
        }
    }

    internal (int Start, int End) GetVisibleRange() => ComputeVisibleRange();

    private (int Start, int End) ComputeVisibleRange(bool? allowOutOfRange = null)
        => TimelineSlotRange.Compute(
            this,
            _scrollViewer,
            ThumbnailCount,
            buffer: 2,
            ContentWidth,
            ContentOffsetX,
            allowOutOfRange ?? IsPreviewActive);

    public List<int> GetMissingIndices(int start, int end) => _thumbnails.GetMissingIndices(start, end);

    private void NotifyVisibleRangeIfChanged()
    {
        var (start, end) = ComputeVisibleRange();
        if (start != _lastNotifiedStart || end != _lastNotifiedEnd)
        {
            _lastNotifiedStart = start;
            _lastNotifiedEnd = end;
            VisibleRangeChanged?.Invoke(start, end);
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

        double contentWidth = !double.IsNaN(ContentWidth) && ContentWidth > 0 ? ContentWidth : width;
        double slotWidth = contentWidth / count;
        if (slotWidth <= 0)
            return;

        double contentOffsetX = ContentOffsetX;
        bool customLayout = HasCustomLayout;
        var (startIndex, endIndex) = ComputeVisibleRange(allowOutOfRange: customLayout);

        if (customLayout)
        {
            using (context.PushClip(new Rect(0, 0, width, height)))
            {
                DrawThumbnails(context, startIndex, endIndex, slotWidth, contentOffsetX, height);
            }
        }
        else
        {
            DrawThumbnails(context, startIndex, endIndex, slotWidth, contentOffsetX, height);
        }
    }

    private void DrawThumbnails(
        DrawingContext context,
        int startIndex,
        int endIndex,
        double slotWidth,
        double contentOffsetX,
        double height)
    {
        for (int i = startIndex; i <= endIndex; i++)
        {
            if (!_thumbnails.TryGetValue(i, out var img) || img == null)
                continue;

            double slotX = contentOffsetX + i * slotWidth;

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
        ClearThumbnails();
        _scrollViewer?.PropertyChanged -= OnScrollViewerPropertyChanged;
        _scrollViewer = null;
    }
}

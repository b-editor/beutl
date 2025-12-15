using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Beutl.Views.Tools.Scopes;

public abstract class ScopeControlBase : Control
{
    public static readonly StyledProperty<WriteableBitmap?> SourceBitmapProperty =
        AvaloniaProperty.Register<ScopeControlBase, WriteableBitmap?>(nameof(SourceBitmap));

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(AxisBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(BackgroundBrush));

    public static readonly StyledProperty<double> AxisMarginProperty =
        AvaloniaProperty.Register<ScopeControlBase, double>(nameof(AxisMargin), 32);

    protected static readonly Typeface DefaultTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private static readonly ArrayPool<byte> s_bytePool = ArrayPool<byte>.Shared;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private CancellationTokenSource? _renderCts;
    private WriteableBitmap? _frontBuffer;
    private WriteableBitmap? _backBuffer;

    protected WriteableBitmap? RenderedBitmap => _frontBuffer;

    public void Refresh()
    {
        OnSourceBitmapChanged();
    }

    static ScopeControlBase()
    {
        AffectsRender<ScopeControlBase>(
            SourceBitmapProperty,
            AxisBrushProperty,
            LabelBrushProperty,
            BackgroundBrushProperty,
            AxisMarginProperty);

        SourceBitmapProperty.Changed.AddClassHandler<ScopeControlBase>((s, e) => s.OnSourceBitmapChanged());
    }

    public WriteableBitmap? SourceBitmap
    {
        get => GetValue(SourceBitmapProperty);
        set => SetValue(SourceBitmapProperty, value);
    }

    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public double AxisMargin
    {
        get => GetValue(AxisMarginProperty);
        set => SetValue(AxisMarginProperty, value);
    }

    protected abstract string[]? VerticalAxisLabels { get; }

    protected abstract string[]? HorizontalAxisLabels { get; }

    protected abstract WriteableBitmap? RenderScope(
        byte[] sourceData,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap);

    private void OnSourceBitmapChanged()
    {
        StartBackgroundRender();
    }

    private async void StartBackgroundRender()
    {
        var bitmap = SourceBitmap;
        if (bitmap == null)
        {
            _frontBuffer?.Dispose();
            _frontBuffer = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            InvalidateVisual();
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken ct = _renderCts.Token;

        byte[] sourceData;
        int sourceWidth, sourceHeight, sourceStride;
        unsafe
        {
            using ILockedFramebuffer frame = bitmap.Lock();
            (sourceWidth, sourceHeight, sourceStride) = (frame.Size.Width, frame.Size.Height, frame.RowBytes);
            int dataLength = sourceStride * sourceHeight;
            sourceData = s_bytePool.Rent(dataLength);
            new ReadOnlySpan<byte>((void*)frame.Address, dataLength).CopyTo(sourceData);
        }

        var backBuffer = _backBuffer;

        bool lockTaken = false;
        try
        {
            await _renderLock.WaitAsync(ct);
            lockTaken = true;
            var bounds = Bounds;
            double axisMargin = AxisMargin;
            int targetWidth = (int)Math.Max(1, bounds.Width - axisMargin);
            int targetHeight = (int)Math.Max(1, bounds.Height - axisMargin);

            if (targetWidth <= 0 || targetHeight <= 0) return;

            await Task.Run(async () =>
            {
                try
                {
                    if (ct.IsCancellationRequested) return;

                    var result = RenderScope(
                        sourceData,
                        sourceWidth,
                        sourceHeight,
                        sourceStride,
                        targetWidth,
                        targetHeight,
                        backBuffer);

                    if (result == null) return;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var oldFront = _frontBuffer;
                        _frontBuffer = result;

                        if (result != backBuffer)
                        {
                            _backBuffer?.Dispose();
                            _backBuffer = oldFront;
                        }
                        else if (oldFront != result)
                        {
                            _backBuffer = oldFront;
                        }

                        InvalidateVisual();
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Scope render error: {ex.Message}");
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            s_bytePool.Return(sourceData);
            if (lockTaken)
                _renderLock.Release();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        StartBackgroundRender();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        double axisMargin = AxisMargin;
        double contentWidth = bounds.Width - axisMargin;
        double contentHeight = bounds.Height - axisMargin;

        // Draw background
        var bgBrush = BackgroundBrush;
        if (bgBrush != null)
        {
            context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        }

        if (contentWidth <= 0 || contentHeight <= 0)
            return;

        var bitmap = _frontBuffer;
        if (bitmap != null)
        {
            var destRect = new Rect(axisMargin, 0, contentWidth, contentHeight);
            using (context.PushRenderOptions(new RenderOptions
                   {
                       BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
                   }))
            {
                context.DrawImage(bitmap, destRect);
            }
        }

        // Draw axes
        DrawAxes(context, bounds, axisMargin, contentWidth, contentHeight);
    }

    private void DrawAxes(DrawingContext context, Rect bounds, double axisMargin, double contentWidth,
        double contentHeight)
    {
        var axisBrush = AxisBrush ?? Brushes.Gray;
        var labelBrush = LabelBrush ?? Brushes.Gray;
        var axisPen = new Pen(axisBrush, 1);

        // Draw vertical axis line
        context.DrawLine(axisPen, new Point(axisMargin, 0), new Point(axisMargin, contentHeight));

        // Draw horizontal axis line
        context.DrawLine(axisPen, new Point(axisMargin, contentHeight), new Point(bounds.Width, contentHeight));

        // Draw vertical labels (from top to bottom)
        var verticalLabels = VerticalAxisLabels;
        if (verticalLabels is { Length: > 0 })
        {
            int count = verticalLabels.Length;
            for (int i = 0; i < count; i++)
            {
                double y = count > 1 ? i * contentHeight / (count - 1) : contentHeight / 2;

                var formattedText = new FormattedText(
                    verticalLabels[i],
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    DefaultTypeface,
                    10,
                    labelBrush);

                double textX = axisMargin - formattedText.Width - 4;
                double textY = y - formattedText.Height / 2;

                context.DrawText(formattedText, new Point(Math.Max(0, textX), textY));
                context.DrawLine(axisPen, new Point(axisMargin - 3, y), new Point(axisMargin, y));
            }
        }

        // Draw horizontal labels (from left to right)
        var horizontalLabels = HorizontalAxisLabels;
        if (horizontalLabels is { Length: > 0 })
        {
            int count = horizontalLabels.Length;
            for (int i = 0; i < count; i++)
            {
                double x = axisMargin + (count > 1 ? i * contentWidth / (count - 1) : contentWidth / 2);

                var formattedText = new FormattedText(
                    horizontalLabels[i],
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    DefaultTypeface,
                    10,
                    labelBrush);

                double textX = x - formattedText.Width / 2;
                double textY = contentHeight + 4;

                context.DrawText(formattedText, new Point(textX, textY));
                context.DrawLine(axisPen, new Point(x, contentHeight), new Point(x, contentHeight + 3));
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        lock (_renderLock)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
        }

        _frontBuffer?.Dispose();
        _frontBuffer = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    protected static uint PackColor(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)(b | (g << 8) | (r << 16) | (a << 24));
    }

    protected static void PlotPoint(Span<uint> dest, int stride, int x, int y, uint color)
    {
        int height = dest.Length / stride;
        if ((uint)x >= (uint)stride || (uint)y >= (uint)height) return;

        int idx = y * stride + x;
        uint existing = dest[idx];
        byte existingA = (byte)(existing >> 24);
        byte newA = (byte)(color >> 24);
        dest[idx] = newA > existingA ? color : BlendAdd(existing, color);
    }

    protected static uint BlendAdd(uint dst, uint src)
    {
        byte db = (byte)(dst);
        byte dg = (byte)(dst >> 8);
        byte dr = (byte)(dst >> 16);
        byte da = (byte)(dst >> 24);

        byte sb = (byte)(src);
        byte sg = (byte)(src >> 8);
        byte sr = (byte)(src >> 16);
        byte sa = (byte)(src >> 24);

        byte a = (byte)Math.Min(255, da + sa);
        byte r = (byte)Math.Min(255, dr + sr * sa / 255);
        byte g = (byte)Math.Min(255, dg + sg * sa / 255);
        byte b = (byte)Math.Min(255, db + sb * sa / 255);

        return PackColor(r, g, b, a);
    }
}

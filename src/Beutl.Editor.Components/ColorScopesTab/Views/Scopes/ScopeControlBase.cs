using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Media.Source;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

public abstract class ScopeControlBase : Control
{
    public static readonly StyledProperty<Ref<BtlBitmap>?> SourceBitmapProperty =
        AvaloniaProperty.Register<ScopeControlBase, Ref<BtlBitmap>?>(nameof(SourceBitmap));

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(AxisBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<ScopeControlBase, IBrush?>(nameof(BackgroundBrush));

    public static readonly StyledProperty<double> AxisMarginProperty =
        AvaloniaProperty.Register<ScopeControlBase, double>(nameof(AxisMargin), 32);

    public static readonly DirectProperty<ScopeControlBase, ScopeColorSpace> ColorSpaceProperty =
        AvaloniaProperty.RegisterDirect<ScopeControlBase, ScopeColorSpace>(
            nameof(ColorSpace), o => o.ColorSpace, (o, v) => o.ColorSpace = v, ScopeColorSpace.Gamma);

    private ScopeColorSpace _colorSpace = ScopeColorSpace.Gamma;

    protected static readonly Typeface DefaultTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private readonly Pen _axisPen = new(Brushes.Gray, 1.5);
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private CancellationTokenSource? _renderCts;
    private WriteableBitmap? _frontBuffer;
    private WriteableBitmap? _backBuffer;
    private bool _isDetached;

    protected WriteableBitmap? RenderedBitmap => _frontBuffer;

    public void Refresh()
    {
        StartBackgroundRender();
    }

    static ScopeControlBase()
    {
        AffectsRender<ScopeControlBase>(
            AxisBrushProperty,
            LabelBrushProperty,
            BackgroundBrushProperty,
            AxisMarginProperty,
            ColorSpaceProperty);

        AxisBrushProperty.Changed.AddClassHandler<ScopeControlBase>((s, e) =>
            s._axisPen.Brush = (e.NewValue as IBrush) ?? Brushes.Gray);

        ColorSpaceProperty.Changed.AddClassHandler<ScopeControlBase>((o, _) => o.Refresh());
    }

    public Ref<BtlBitmap>? SourceBitmap
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

    public ScopeColorSpace ColorSpace
    {
        get => _colorSpace;
        set => SetAndRaise(ColorSpaceProperty, ref _colorSpace, value);
    }

    protected abstract string[]? VerticalAxisLabels { get; }

    protected abstract string[]? HorizontalAxisLabels { get; }

    protected abstract WriteableBitmap? RenderScope(
        BtlBitmap sourceBitmap,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap);

    private async void StartBackgroundRender()
    {
        CancelRender();
        if (_isDetached) return;

        var bitmapRef = SourceBitmap?.TryClone();
        if (bitmapRef == null)
        {
            ClearBuffers();
            InvalidateVisual();
            return;
        }

        using var renderCts = new CancellationTokenSource();
        _renderCts = renderCts;
        var ct = renderCts.Token;
        WriteableBitmap? backBuffer = null;
        WriteableBitmap? result = null;

        bool lockTaken = false;
        try
        {
            await _renderLock.WaitAsync(ct);
            lockTaken = true;
            ct.ThrowIfCancellationRequested();
            // The worker owns this buffer until it completes or transfers the result back to the UI.
            backBuffer = _backBuffer;
            _backBuffer = null;
            var bounds = Bounds;
            double axisMargin = AxisMargin;
            int targetWidth = (int)Math.Max(1, bounds.Width - axisMargin);
            int targetHeight = (int)Math.Max(1, bounds.Height - axisMargin);

            if (targetWidth <= 0 || targetHeight <= 0) return;

            result = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return RenderScope(bitmapRef.Value, targetWidth, targetHeight, backBuffer);
            }, ct);
            if (result == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(_renderCts, renderCts)) return;

                _backBuffer = _frontBuffer;
                _frontBuffer = result;
                if (ReferenceEquals(result, backBuffer))
                    backBuffer = null;
                result = null;
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
        finally
        {
            result?.Dispose();
            if (!ReferenceEquals(backBuffer, result))
                backBuffer?.Dispose();
            bitmapRef.Dispose();
            if (ReferenceEquals(_renderCts, renderCts))
                _renderCts = null;
            if (lockTaken)
                _renderLock.Release();
        }
    }

    private void CancelRender()
    {
        _renderCts?.Cancel();
        _renderCts = null;
    }

    private void ClearBuffers()
    {
        _frontBuffer?.Dispose();
        _frontBuffer = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
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
        var labelBrush = LabelBrush ?? Brushes.Gray;

        // Draw vertical axis line
        context.DrawLine(_axisPen, new Point(axisMargin, 0), new Point(axisMargin, contentHeight));

        // Draw horizontal axis line
        context.DrawLine(_axisPen, new Point(axisMargin, contentHeight), new Point(bounds.Width, contentHeight));

        DrawVerticalLabels(context, VerticalAxisLabels, labelBrush, axisMargin, contentHeight);
        DrawHorizontalLabels(context, HorizontalAxisLabels, labelBrush, bounds, axisMargin, contentWidth, contentHeight);
    }

    // Draw vertical labels (from top to bottom)
    private void DrawVerticalLabels(
        DrawingContext context, string[]? verticalLabels, IBrush labelBrush, double axisMargin, double contentHeight)
    {
        if (verticalLabels is { Length: > 0 })
        {
            int count = verticalLabels.Length;
            for (int i = 0; i < count; i++)
            {
                double y = count > 1 ? i * contentHeight / (count - 1) : contentHeight / 2;

                var formattedText = CreateAxisLabel(verticalLabels[i], labelBrush);

                double textX = axisMargin - formattedText.Width - 4;
                double textY = y - formattedText.Height / 2;
                if (i == 0)
                {
                    textY = 4;
                }
                else if (i == count - 1)
                {
                    textY = contentHeight - formattedText.Height;
                }

                context.DrawText(formattedText, new Point(Math.Max(0, textX), textY));
                context.DrawLine(_axisPen, new Point(axisMargin - 3, y), new Point(axisMargin, y));
            }
        }
    }

    // Draw horizontal labels (from left to right)
    private void DrawHorizontalLabels(
        DrawingContext context, string[]? horizontalLabels, IBrush labelBrush, Rect bounds, double axisMargin,
        double contentWidth, double contentHeight)
    {
        if (horizontalLabels is { Length: > 0 })
        {
            int count = horizontalLabels.Length;
            for (int i = 0; i < count; i++)
            {
                double x = axisMargin + (count > 1 ? i * contentWidth / (count - 1) : contentWidth / 2);

                var formattedText = CreateAxisLabel(horizontalLabels[i], labelBrush);

                double textX = x - formattedText.Width / 2;
                double textY = contentHeight + 4;
                if (i == 0)
                {
                    textX = axisMargin;
                }
                else if (i == count - 1)
                {
                    textX = bounds.Width - formattedText.Width;
                }

                context.DrawText(formattedText, new Point(textX, textY));
                context.DrawLine(_axisPen, new Point(x, contentHeight), new Point(x, contentHeight + 3));
            }
        }
    }

    private static FormattedText CreateAxisLabel(string text, IBrush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            10,
            brush);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isDetached = false;
        StartBackgroundRender();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _isDetached = true;
        CancelRender();
        ClearBuffers();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static uint PackColor(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)(b | (g << 8) | (r << 16) | (a << 24));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

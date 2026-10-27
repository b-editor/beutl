using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Beutl.Configuration;
using Beutl.Media.Source;
using SkiaSharp;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Controls;

public class BitmapView : Avalonia.Controls.Control
{
    public static readonly StyledProperty<Ref<BtlBitmap>?> SourceProperty =
        AvaloniaProperty.Register<BitmapView, Ref<BtlBitmap>?>(nameof(Source));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<BitmapView, Stretch>(nameof(Stretch), Stretch.Uniform);

    public static readonly StyledProperty<BitmapInterpolationMode> InterpolationModeProperty =
        AvaloniaProperty.Register<BitmapView, BitmapInterpolationMode>(
            nameof(InterpolationMode), BitmapInterpolationMode.HighQuality);

    public static readonly StyledProperty<UIToneMappingOperator> ToneMappingProperty =
        AvaloniaProperty.Register<BitmapView, UIToneMappingOperator>(nameof(ToneMapping), UIToneMappingOperator.None);

    public static readonly StyledProperty<float> ToneMappingExposureProperty =
        AvaloniaProperty.Register<BitmapView, float>(nameof(ToneMappingExposure), 0f);

    private Size? _lastSourceSize;
    private Ref<BtlBitmap>? _clonedSource;

    static BitmapView()
    {
        AffectsRender<BitmapView>(SourceProperty, StretchProperty, InterpolationModeProperty,
            ToneMappingProperty, ToneMappingExposureProperty);
        AffectsMeasure<BitmapView>(StretchProperty);
    }

    public Ref<BtlBitmap>? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public BitmapInterpolationMode InterpolationMode
    {
        get => GetValue(InterpolationModeProperty);
        set => SetValue(InterpolationModeProperty, value);
    }

    public UIToneMappingOperator ToneMapping
    {
        get => GetValue(ToneMappingProperty);
        set => SetValue(ToneMappingProperty, value);
    }

    public float ToneMappingExposure
    {
        get => GetValue(ToneMappingExposureProperty);
        set => SetValue(ToneMappingExposureProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            Ref<BtlBitmap>? source = Source;
            Ref<BtlBitmap>? clone = source?.TryClone();
            // Playback can dispose a superseded ref before its queued binding reaches the UI.
            // Keep the displayed frame until a live replacement arrives; null explicitly clears it.
            if (source != null && clone == null)
                return;

            Ref<BtlBitmap>? previous = _clonedSource;
            _clonedSource = clone;
            previous?.Dispose();

            var oldSize = _lastSourceSize;
            _lastSourceSize = GetSize();
            if (oldSize != _lastSourceSize)
            {
                InvalidateMeasure();
            }
        }
    }

    private Size? GetSize()
    {
        var source = _clonedSource;
        return source?.Value is { IsDisposed: false, Width: var width, Height: var height }
            ? new Size(width, height)
            : null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_lastSourceSize == null)
            return default;

        return Stretch.CalculateSize(availableSize, _lastSourceSize.Value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_lastSourceSize == null)
            return default;

        return Stretch.CalculateSize(finalSize, _lastSourceSize.Value);
    }

    public override void Render(DrawingContext context)
    {
        Ref<BtlBitmap>? cloneForDrawOp = _clonedSource?.TryClone();
        if (cloneForDrawOp == null) return;

        try
        {
            var viewPort = new Rect(Bounds.Size);
            var sourceSize = new Size(cloneForDrawOp.Value.Width, cloneForDrawOp.Value.Height);
            var scale = Stretch.CalculateScaling(Bounds.Size, sourceSize);
            var scaledSize = sourceSize * scale;
            var destRect = viewPort
                .CenterRect(new Rect(scaledSize))
                .Intersect(viewPort);
            var sourceRect = new Rect(sourceSize)
                .CenterRect(new Rect(destRect.Size / scale));

            var skSourceRect = SKRect.Create(
                (float)sourceRect.X, (float)sourceRect.Y,
                (float)sourceRect.Width, (float)sourceRect.Height);
            var skDestRect = SKRect.Create(
                (float)destRect.X, (float)destRect.Y,
                (float)destRect.Width, (float)destRect.Height);

            context.Custom(new BitmapDrawOperation(
                new Rect(Bounds.Size),
                cloneForDrawOp,
                skSourceRect,
                skDestRect,
                InterpolationMode,
                ToneMapping,
                ToneMappingExposure));
        }
        catch
        {
            cloneForDrawOp.Dispose();
        }
    }

    /// <summary>
    /// Builds a paint for drawing a preview frame onto the screen surface. Dithering is mandatory:
    /// the surface is 8-bit while frames are linear RgbaF16, and this path never passes through
    /// <see cref="BtlBitmap.Convert"/>, so without it gradients band on screen.
    /// </summary>
    internal static SKPaint CreatePreviewPaint(SKColorFilter? colorFilter)
    {
        return new SKPaint { ColorFilter = colorFilter, IsDither = true };
    }

    private sealed class BitmapDrawOperation(
        Rect bounds,
        Ref<BtlBitmap> bitmap,
        SKRect sourceRect,
        SKRect destRect,
        BitmapInterpolationMode interpolationMode,
        UIToneMappingOperator tmOperator,
        float tmExposure)
        : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Dispose()
        {
            bitmap.Dispose();
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return ReferenceEquals(this, other);
        }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            PreviewFrameRenderer.Draw(canvas, bitmap.Value, sourceRect, destRect,
                interpolationMode, tmOperator, tmExposure);
        }
    }
}

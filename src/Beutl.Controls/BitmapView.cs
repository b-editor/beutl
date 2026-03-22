#nullable enable

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
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

    private Size? _lastSourceSize;
    private Ref<BtlBitmap>? _clonedSource;

    static BitmapView()
    {
        AffectsRender<BitmapView>(SourceProperty, StretchProperty, InterpolationModeProperty);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            if (_clonedSource != null)
            {
                _clonedSource.Dispose();
                _clonedSource = null;
            }

            if (Source != null)
            {
                _clonedSource = Source.TryClone();
            }

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
        var source = Source;
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
                InterpolationMode));
        }
        catch
        {
            cloneForDrawOp.Dispose();
        }
    }

    private sealed class BitmapDrawOperation(
        Rect bounds,
        Ref<BtlBitmap> bitmap,
        SKRect sourceRect,
        SKRect destRect,
        BitmapInterpolationMode interpolationMode)
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

            using var image = SKImage.FromBitmap(bitmap.Value.SKBitmap);
            if (image == null)
                return;

            var sampling = interpolationMode switch
            {
                BitmapInterpolationMode.None => SKSamplingOptions.Default,
                BitmapInterpolationMode.LowQuality => new SKSamplingOptions(SKFilterMode.Linear),
                BitmapInterpolationMode.MediumQuality =>
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                BitmapInterpolationMode.HighQuality => new SKSamplingOptions(SKCubicResampler.Mitchell),
                _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            };

            using var paint = new SKPaint();
            canvas.DrawImage(image, sourceRect, destRect, sampling, paint);
        }
    }
}

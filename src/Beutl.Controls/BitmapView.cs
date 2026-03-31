#nullable enable

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
                InterpolationMode,
                ToneMapping,
                ToneMappingExposure));
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
        BitmapInterpolationMode interpolationMode,
        UIToneMappingOperator tmOperator,
        float tmExposure)
        : ICustomDrawOperation
    {
        private static readonly SKPaint s_linearPaint = new() { ColorFilter = SKColorFilter.CreateLinearToSrgbGamma() };

        private static readonly SKPaint s_gammaPaint = new();

        private static readonly SKRuntimeEffect? s_toneMappingEffect;

        static BitmapDrawOperation()
        {
            string sksl = """
                uniform shader src;
                uniform float exposure;
                uniform int tmOperator;

                float3 linearToSrgb(float3 c) {
                    float3 lo = c * 12.92;
                    float3 hi = 1.055 * pow(max(c, float3(0.0)), float3(1.0/2.4)) - 0.055;
                    return mix(lo, hi, step(float3(0.0031308), c));
                }

                float3 reinhard(float3 c) {
                    return c / (1.0 + c);
                }

                float3 aces(float3 c) {
                    const float a = 2.51;
                    const float b = 0.03;
                    const float cc = 2.43;
                    const float d = 0.59;
                    const float e = 0.14;
                    return clamp((c * (a * c + b)) / (c * (cc * c + d) + e), 0.0, 1.0);
                }

                float3 hable_partial(float3 x) {
                    const float A = 0.15;
                    const float B = 0.50;
                    const float C = 0.10;
                    const float D = 0.20;
                    const float E = 0.02;
                    const float F = 0.30;
                    return ((x * (A*x + C*B) + D*E) / (x * (A*x + B) + D*F)) - E/F;
                }

                float3 hable(float3 c) {
                    float3 W = float3(11.2);
                    return hable_partial(c) / hable_partial(W);
                }

                half4 main(float2 coord) {
                    half4 c = src.eval(coord);
                    float alpha = c.a;
                    if (alpha <= 0.0001) return half4(0.0);

                    float3 rgb = c.rgb / alpha;
                    rgb *= exp2(exposure);

                    if (tmOperator == 1) {
                        rgb = reinhard(max(rgb, float3(0.0)));
                    } else if (tmOperator == 2) {
                        rgb = aces(max(rgb, float3(0.0)));
                    } else if (tmOperator == 3) {
                        rgb = hable(max(rgb, float3(0.0)));
                    }

                    rgb = clamp(rgb, 0.0, 1.0);
                    rgb = linearToSrgb(rgb);

                    return half4(half3(rgb * alpha), half(alpha));
                }
                """;

            s_toneMappingEffect = SKRuntimeEffect.CreateShader(sksl, out _);
        }

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

            bool isLinear = image.ColorSpace?.GammaIsLinear == true;
            bool needsToneMapping = isLinear && tmOperator != UIToneMappingOperator.None && s_toneMappingEffect != null;

            if (needsToneMapping)
            {
                // トーンマッピングシェーダーを使用
                float scaleX = sourceRect.Width / destRect.Width;
                float scaleY = sourceRect.Height / destRect.Height;
                float transX = sourceRect.Left - destRect.Left * scaleX;
                float transY = sourceRect.Top - destRect.Top * scaleY;
                var localMatrix = SKMatrix.CreateScaleTranslation(1 / scaleX, 1 / scaleY, transX, transY);

                using var imageShader = image.ToShader(
                    SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    sampling, localMatrix);
                var builder = new SKRuntimeShaderBuilder(s_toneMappingEffect!);
                builder.Children["src"] = imageShader;
                builder.Uniforms["exposure"] = tmExposure;
                builder.Uniforms["tmOperator"] = (int)tmOperator;

                using var finalShader = builder.Build();
                using var paint = new SKPaint();
                paint.Shader = finalShader;
                canvas.DrawRect(destRect, paint);
            }
            else
            {
                // 既存パス: Linear→sRGB or そのまま
                canvas.DrawImage(image, sourceRect, destRect, sampling,
                    isLinear ? s_linearPaint : s_gammaPaint);
            }
        }
    }
}

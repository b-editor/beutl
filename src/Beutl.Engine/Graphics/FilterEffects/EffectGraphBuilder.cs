using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The declarative recording surface an effect appends node descriptors to (feature 004, data-model §1,
/// research D1) — the replacement for <see cref="FilterEffectContext"/>'s recording role. It preserves today's
/// append idiom: each primitive appender advances the logical <see cref="Bounds"/> by the node's forward bounds,
/// and the convenience methods construct the same vocabulary (<c>Blur</c>, <c>Saturate</c>, …) as descriptors.
/// The builder never renders or allocates; it produces an <see cref="EffectGraph"/> the render node compiles and
/// executes (A1). Payloads are validated on append so authoring errors surface at describe time, not execute time.
/// </summary>
public sealed class EffectGraphBuilder
{
    private readonly List<EffectNode> _nodes = [];
    private readonly List<IDisposable> _disposables = [];

    internal EffectGraphBuilder(Rect bounds, float outputScale, float workingScale)
    {
        OriginalBounds = bounds;
        Bounds = bounds;
        OutputScale = outputScale;
        WorkingScale = workingScale;
    }

    /// <summary>The current logical bounds, advanced by each appended node's forward bounds.</summary>
    public Rect Bounds { get; private set; }

    /// <summary>The input logical bounds this describe pass started from.</summary>
    public Rect OriginalBounds { get; }

    /// <summary>The render request's output scale <c>s_out</c> (never a ceiling on working scale).</summary>
    public float OutputScale { get; }

    /// <summary>The working density <c>w</c> the render node resolved for this boundary (FR-012); read-only to authors.</summary>
    public float WorkingScale { get; }

    /// <summary>Appends a shader node (snippet or whole-source).</summary>
    public EffectGraphBuilder Shader(ShaderNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a color-filter node (always coordinate-invariant, always fusable).</summary>
    public EffectGraphBuilder ColorFilter(ColorFilterNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a Skia image-filter node.</summary>
    public EffectGraphBuilder SkiaFilter(SkiaFilterNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    internal EffectGraphBuilder AppendOpaqueLegacy(FilterEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _disposables.Add(context);
        return Append(new OpaqueLegacyNodeDescriptor(context));
    }

    private EffectGraphBuilder Append(EffectNodeDescriptor descriptor)
    {
        Rect input = Bounds;
        Rect output = descriptor.Bounds.IsRenderTimeResolved
            ? Rect.Invalid
            : descriptor.Bounds.TransformBounds(input);
        _nodes.Add(new EffectNode(descriptor, input, output));
        if (!output.IsInvalid)
        {
            Bounds = output;
        }

        return this;
    }

    internal EffectGraph Build() => new(_nodes, OriginalBounds, OutputScale, WorkingScale, _disposables);

    // ---- Convenience vocabulary (mirrors today's FilterEffectContext) ---------------------------------

    /// <summary>Appends a Gaussian blur (Skia filter), inflating bounds by <c>sigma × 3</c>.</summary>
    public EffectGraphBuilder Blur(Size sigma)
    {
        if (sigma.Width < 0) sigma = sigma.WithWidth(0);
        if (sigma.Height < 0) sigma = sigma.WithHeight(0);

        var inflate = new Thickness(sigma.Width * 3, sigma.Height * 3);
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => sigma.Width == 0 && sigma.Height == 0
                ? inner
                : SKImageFilter.CreateBlur(sigma.Width, sigma.Height, inner),
            InflateContract(inflate),
            structuralToken: "Blur"));
    }

    /// <summary>Appends a drop shadow that keeps the source (union of source and shadow bounds).</summary>
    public EffectGraphBuilder DropShadow(Point position, Size sigma, Color color)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDropShadow(
                (float)position.X, (float)position.Y, sigma.Width, sigma.Height, color.ToSKColor(), inner),
            BoundsContract.Create(
                r => r.Union(r.Translate(position).Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3))),
                r => r,
                isRenderTimeResolved: false),
            structuralToken: "DropShadow"));
    }

    /// <summary>Appends a drop shadow that replaces the source with only the shadow.</summary>
    public EffectGraphBuilder DropShadowOnly(Point position, Size sigma, Color color)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDropShadowOnly(
                (float)position.X, (float)position.Y, sigma.Width, sigma.Height, color.ToSKColor(), inner),
            BoundsContract.Create(
                r => r.Translate(position).Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)),
                r => r),
            structuralToken: "DropShadowOnly"));
    }

    /// <summary>Appends a morphological erode.</summary>
    public EffectGraphBuilder Erode(float radiusX, float radiusY)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateErode(radiusX, radiusY, inner),
            BoundsContract.Create(r => r, r => r),
            structuralToken: "Erode"));
    }

    /// <summary>Appends a morphological dilate, inflating bounds by the radius.</summary>
    public EffectGraphBuilder Dilate(float radiusX, float radiusY)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDilate(radiusX, radiusY, inner),
            InflateContract(new Thickness(radiusX, radiusY)),
            structuralToken: "Dilate"));
    }

    /// <summary>Appends a matrix transform (Skia filter), mapping bounds through <paramref name="matrix"/>.</summary>
    public EffectGraphBuilder Transform(Matrix matrix, BitmapInterpolationMode interpolation)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateMatrix(matrix.ToSKMatrix(), interpolation.ToSKSamplingOptions(), inner),
            BoundsContract.Create(r => r.TransformToAABB(matrix), r => r),
            structuralToken: "Transform"));
    }

    /// <summary>Appends a matrix convolution (Skia filter).</summary>
    public EffectGraphBuilder MatrixConvolution(
        PixelSize kernelSize, float[] kernel, float gain, float bias, PixelPoint kernelOffset,
        GradientSpreadMethod spreadMethod, bool convolveAlpha)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        int w = kernelSize.Width - 1;
        int h = kernelSize.Height - 1;
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateMatrixConvolution(
                kernelSize.ToSKSizeI(), kernel, gain, bias, kernelOffset.ToSKPointI(),
                spreadMethod.ToSKShaderTileMode(), convolveAlpha, inner),
            BoundsContract.Create(
                r => r.Inflate(new Thickness(kernelOffset.X - w, kernelOffset.Y - h, kernelOffset.X, kernelOffset.Y)),
                r => r),
            structuralToken: "MatrixConvolution"));
    }

    /// <summary>Appends an arbitrary color matrix as a color-filter node.</summary>
    public EffectGraphBuilder ColorMatrix(ColorMatrix matrix)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                matrix.ToArrayForSkia(array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "ColorMatrix"));
    }

    /// <summary>Appends a saturation adjustment as a color-filter node.</summary>
    public EffectGraphBuilder Saturate(float amount)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateSaturateMatrix(amount, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Saturate"));
    }

    /// <summary>Appends a hue rotation as a color-filter node.</summary>
    public EffectGraphBuilder HueRotate(float degrees)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "HueRotate"));
    }

    /// <summary>Appends a brightness adjustment as a color-filter node.</summary>
    public EffectGraphBuilder Brightness(float amount)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateBrightness(amount, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Brightness"));
    }

    /// <summary>Appends Skia's high-contrast color filter.</summary>
    public EffectGraphBuilder HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateHighContrast(
                grayscale, (SKHighContrastConfigInvertStyle)invertStyle, contrast),
            structuralToken: "HighContrast"));
    }

    /// <summary>Appends a linear-light lighting color matrix (multiply + add).</summary>
    public EffectGraphBuilder Lighting(Color multiply, Color add)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                var mul = multiply.ToLinear();
                var addLinear = add.ToLinear();
                float[] array = new float[20];
                array[0] = mul.X;
                array[6] = mul.Y;
                array[12] = mul.Z;
                array[18] = 1;
                array[4] = addLinear.X;
                array[9] = addLinear.Y;
                array[14] = addLinear.Z;
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Lighting"));
    }

    /// <summary>Appends Skia's luma-to-color color filter.</summary>
    public EffectGraphBuilder LumaColor()
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateLumaColor(), structuralToken: "LumaColor"));
    }

    /// <summary>Appends a luminance-to-alpha color matrix.</summary>
    public EffectGraphBuilder LuminanceToAlpha()
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateLuminanceToAlphaMatrix(array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "LuminanceToAlpha"));
    }

    /// <summary>Appends a blend-mode color filter against a constant color.</summary>
    public EffectGraphBuilder BlendMode(Color color, BlendMode blendMode)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateBlendMode(color.ToSKColor(), (SKBlendMode)blendMode),
            structuralToken: "BlendMode"));
    }

    private static BoundsContract InflateContract(Thickness inflate)
        => BoundsContract.Create(r => r.Inflate(inflate), r => r.Inflate(inflate));
}

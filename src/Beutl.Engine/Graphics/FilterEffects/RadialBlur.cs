using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.RadialBlur), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RadialBlur : FilterEffect
{
    private const int MaxSamples = 128;

    private const string ShaderSource = """
        #version 450
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(set = 0, binding = 1) uniform sampler2D sampleTransforms;
        layout(push_constant) uniform PushConstants {
            layout(offset = 0) vec2 center;
            layout(offset = 8) vec2 sourceOrigin;
            layout(offset = 16) ivec4 sourceSubset;
            layout(offset = 32) float sourceScale;
            layout(offset = 36) int sampleCount;
        } pc;

        void main() {
            vec2 offset = gl_FragCoord.xy - pc.center;
            vec4 color = vec4(0.0);
            for (int i = 0; i < pc.sampleCount; i++) {
                vec2 t = texelFetch(sampleTransforms, ivec2(i, 0), 0).xy;
                vec2 sampleOffset = vec2(
                    t.x * offset.x - t.y * offset.y,
                    t.y * offset.x + t.x * offset.y);
                vec2 samplePosition = (pc.center + sampleOffset - pc.sourceOrigin) * pc.sourceScale;
                ivec2 p = ivec2(floor(samplePosition));
                if (all(greaterThanEqual(p, pc.sourceSubset.xy)) && all(lessThan(p, pc.sourceSubset.zw))) {
                    color += texelFetch(srcTexture, p, 0);
                }
            }
            // Accumulate in float32; the destination stores premultiplied linear RGBA16F.
            outColor = color / float(pc.sampleCount);
        }
        """;

    // One fixed pipeline for the current shared graphics context. Failed creation can be retried.
    private static readonly object s_shaderLock = new();
    private static IGraphicsContext? s_shaderContext;
    private static GLSLShader? s_shader;

    public RadialBlur()
    {
        ScanProperties<RadialBlur>();
    }

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Mode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RadialBlurMode> Mode { get; } = Property.Create(RadialBlurMode.Zoom);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Center), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Amount),
        Description = nameof(GraphicsStrings.RadialBlur_Amount_Description), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(10f);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Samples), ResourceType = typeof(GraphicsStrings))]
    [Range(2, MaxSamples)]
    public IProperty<int> Samples { get; } = Property.CreateAnimatable(32);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Interpolation and detached resources can bypass the editor's ranges.
        if (!float.IsFinite(r.Amount) || r.Amount <= 0
            || !float.IsFinite(r.Center.Point.X) || !float.IsFinite(r.Center.Point.Y)
            || r.Mode is not (RadialBlurMode.Zoom or RadialBlurMode.Spin))
            return;

        var data = new EffectData(r.Mode, r.Center, MathF.Min(r.Amount, 100), Math.Clamp(r.Samples, 2, MaxSamples));
        context.CustomEffect(data, OnApplyTo, static (state, bounds) => state.TransformBounds(bounds));
    }

    private static GLSLShader GetOrCreateShader()
    {
        lock (s_shaderLock)
        {
            IGraphicsContext? graphicsContext = GraphicsContextFactory.SharedContext;
            if (!ReferenceEquals(s_shaderContext, graphicsContext))
            {
                GLSLShader? previous = s_shader;
                s_shader = null;
                s_shaderContext = graphicsContext;
                previous?.Dispose();
            }

            return s_shader ??= GLSLShader.CreateBuiltIn(ShaderSource, inputCount: 2);
        }
    }

    private static void OnApplyTo(EffectData data, CustomFilterEffectContext context)
    {
        if (GraphicsContextFactory.SharedContext is not { } graphicsContext
            || context.Targets.Any(target => target.RenderTarget?.Texture is null))
        {
            throw new NotSupportedException("Radial blur requires GPU textures and GLSL execution.");
        }

        Point center = data.ResolveCenter(context.Targets.CalculateBounds());
        GLSLShader shader = GetOrCreateShader();
        // A small float32 lookup texture uses the existing two-input GLSL API without a shared uniform ABI.
        Span<Vector4> samples = stackalloc Vector4[data.Samples];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = new Vector4(data.GetSampleTransform(i), 0, 0);
        using ITexture2D transforms = graphicsContext.CreateTexture2D(samples.Length, 1, TextureFormat.RGBA32Float);
        transforms.Upload(MemoryMarshal.AsBytes(samples));

        context.ForEach((_, input) =>
        {
            EffectTarget output = context.CreateNativeTarget(data.TransformBounds(input.Bounds, center));
            if (output.IsEmpty)
            {
                output.Dispose();
                return input;
            }

            try
            {
                if (output.RenderTarget?.Texture is not { } destination)
                    throw new InvalidOperationException("Failed to allocate a GPU target for radial blur.");
                if (destination.Format != TextureFormat.RGBA16Float)
                    throw new NotSupportedException("Radial blur requires an RGBA16F output target.");

                input.RenderTarget!.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                shader.ExecuteSingleTargetWithMask(
                    input.RenderTarget.Texture!, transforms, destination, CreatePushConstants(data, center, input, output));
                shader.SubmitPendingCommands();
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        });
    }

    private static PushConstants CreatePushConstants(EffectData data, Point center, EffectTarget input, EffectTarget output)
    {
        float scale = output.Scale.Value;
        Vector centerOffset = (center - output.RasterBounds.Position) * scale;
        Vector sourceOrigin = (input.RasterBounds.Position - output.RasterBounds.Position) * scale;
        float inputScale = input.Scale.Value;
        Vector gridOffset = input.DeviceBounds.ToRect(inputScale).Position - input.RasterBounds.Position;
        PixelRect semantic = PixelRect.FromRect(input.Bounds.Translate(gridOffset), inputScale);
        var subset = new PixelRect(
            semantic.X - input.DeviceBounds.X, semantic.Y - input.DeviceBounds.Y,
            semantic.Width, semantic.Height);
        subset = subset.Intersect(new PixelRect(new PixelSize(input.RenderTarget!.Width, input.RenderTarget.Height)));

        return new PushConstants
        {
            Center = new Vector2(centerOffset.X, centerOffset.Y),
            SourceOrigin = new Vector2(sourceOrigin.X, sourceOrigin.Y),
            SourceLeft = subset.X,
            SourceTop = subset.Y,
            SourceRight = subset.Right,
            SourceBottom = subset.Bottom,
            SourceScale = inputScale / scale,
            SampleCount = data.Samples,
        };
    }

    private static Point Transform(Point point, Point center, Vector2 transform)
    {
        Point offset = point - center;
        return center + new Point(
            transform.X * offset.X - transform.Y * offset.Y,
            transform.Y * offset.X + transform.X * offset.Y);
    }

    private readonly record struct EffectData(RadialBlurMode Mode, RelativePoint Center, float Amount, int Samples)
    {
        public Point ResolveCenter(Rect bounds) => bounds.Position + Center.ToPixels(bounds.Size);

        // Rendering and bounds use the same CPU-computed, float32 sample transforms.
        public Vector2 GetSampleTransform(int index)
        {
            float progress = (float)index / (Samples - 1);
            if (Mode == RadialBlurMode.Zoom)
                return new Vector2(1 / (1 + Amount / 100 * progress), 0);

            float angle = (progress - 0.5f) * Amount * (MathF.PI / 180);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        public Rect TransformBounds(Rect bounds)
            => TransformBounds(bounds, ResolveCenter(bounds));

        public Rect TransformBounds(Rect bounds, Point origin)
        {
            if (bounds.IsEmpty)
                return bounds;

            Rect result = bounds;
            for (int i = 0; i < Samples; i++)
            {
                Vector2 inverse = GetSampleTransform(i);
                var forward = new Vector2(inverse.X, -inverse.Y) / inverse.LengthSquared();
                Point a = Transform(bounds.TopLeft, origin, forward);
                Point b = Transform(bounds.TopRight, origin, forward);
                Point c = Transform(bounds.BottomLeft, origin, forward);
                Point d = Transform(bounds.BottomRight, origin, forward);
                float left = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
                float top = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
                float right = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
                float bottom = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));
                result = result.Union(new Rect(left, top, right - left, bottom - top));
            }

            return result;
        }
    }

    // Field offsets match the GLSL push-constant block; all data fits within the existing 128-byte API.
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public Vector2 Center;
        public Vector2 SourceOrigin;
        public int SourceLeft;
        public int SourceTop;
        public int SourceRight;
        public int SourceBottom;
        public float SourceScale;
        public int SampleCount;
    }
}

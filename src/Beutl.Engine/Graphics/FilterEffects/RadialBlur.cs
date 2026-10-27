using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.RadialBlur), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RadialBlur : FilterEffect
{
    private const int MaxSamples = 128;

    private static readonly ShaderProgram s_program = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D srcTexture;
        layout(binding = 1) uniform SourceFrame { vec4 bounds; vec4 raster; vec4 pixels; } source;
        layout(binding = 2) uniform OutputFrame { vec4 bounds; vec4 raster; vec4 pixels; } outputFrame;
        layout(std430, binding = 3) readonly buffer Samples { vec2 values[]; } samples;
        layout(push_constant) uniform Parameters { vec2 center; } pc;

        void main() {
            vec2 position = outputFrame.raster.xy + gl_FragCoord.xy / outputFrame.pixels.z;
            vec2 offset = position - pc.center;
            ivec2 lower = max(ivec2(0), ivec2(floor((source.bounds.xy - source.raster.xy) * source.pixels.z)));
            ivec2 upper = min(textureSize(srcTexture, 0),
                ivec2(ceil((source.bounds.xy + source.bounds.zw - source.raster.xy) * source.pixels.z)));
            vec4 color = vec4(0.0);
            for (int i = 0; i < samples.values.length(); i++) {
                vec2 t = samples.values[i];
                vec2 sampleOffset = vec2(
                    t.x * offset.x - t.y * offset.y,
                    t.y * offset.x + t.x * offset.y);
                ivec2 p = ivec2(floor((pc.center + sampleOffset - source.raster.xy) * source.pixels.z));
                if (all(greaterThanEqual(p, lower)) && all(lessThan(p, upper)))
                    color += texelFetch(srcTexture, p, 0);
            }
            outColor = color / float(samples.values.length());
        }
        """));

    public RadialBlur()
    {
        ScanProperties<RadialBlur>();
    }

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Mode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RadialBlurMode> Mode { get; } = Property.CreateExpression(RadialBlurMode.Zoom);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Center), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Amount),
        Description = nameof(GraphicsStrings.RadialBlur_Amount_Description), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(10f);

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Samples), ResourceType = typeof(GraphicsStrings))]
    [Range(2, MaxSamples)]
    public IExpressionProperty<int> Samples { get; } = Property.CreateAnimatable(32);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Interpolation and detached resources can bypass the editor's ranges.
        if (!float.IsFinite(r.Amount) || r.Amount <= 0
            || !float.IsFinite(r.Center.Point.X) || !float.IsFinite(r.Center.Point.Y)
            || r.Mode is not (RadialBlurMode.Zoom or RadialBlurMode.Spin))
            return;

        var data = new EffectData(r.Mode, r.Center, MathF.Min(r.Amount, 100), Math.Clamp(r.Samples, 2, MaxSamples));
        context.ShaderGraph(data, static (graph, state) =>
        {
            var samples = new Vector2[state.Samples];
            var transforms = new Matrix[state.Samples];
            for (int i = 0; i < samples.Length; i++)
            {
                Vector2 inverse = samples[i] = state.GetSampleTransform(i);
                var forward = new Vector2(inverse.X, -inverse.Y) / inverse.LengthSquared();
                transforms[i] = new Matrix(forward.X, forward.Y, -forward.Y, forward.X, 0, 0);
            }
            ShaderBuffer sampleBuffer = graph.Buffer<Vector2>("samples", samples);
            ShaderImage output = graph.Image2D("radial-blur", graph.Source);
            output.BoundsTransforms = [.. transforms];
            output.BoundsOrigin = state.Center;
            graph.Fullscreen("radial-blur", s_program, pass =>
            {
                pass.SampledImage(0, graph.Source, SamplerState.NearestClamp);
                pass.ImageParameters(1, graph.Source);
                pass.ImageParameters(2, output);
                pass.StorageBuffer(3, sampleBuffer, ResourceAccess.Read);
                pass.PushConstants(Vector2.Zero);
                pass.BoundsOriginConstants(state.Center, 0);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });
    }

    private readonly record struct EffectData(RadialBlurMode Mode, RelativePoint Center, float Amount, int Samples)
    {
        // Rendering and bounds use the same CPU-computed, float32 sample transforms.
        public Vector2 GetSampleTransform(int index)
        {
            float progress = (float)index / (Samples - 1);
            if (Mode == RadialBlurMode.Zoom)
                return new Vector2(1 / (1 + Amount / 100 * progress), 0);

            float angle = (progress - 0.5f) * Amount * (MathF.PI / 180);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }
}

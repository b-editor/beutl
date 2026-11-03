using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Spreads each point of the picture over the shape of a lens aperture. The samples of one round are too far apart at
/// a large size, so each round fills the gaps that the one before left, with the same shape made smaller. The samples
/// depend on the settings alone, so a frame looks the same however playback or a seek got to it.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Bokeh), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Bokeh : FilterEffect
{
    private const int FillRings = 2;
    private const float FillRatio = 2 / 3f;

    public Bokeh() => ScanProperties<Bokeh>();

    /// <summary>How far a point spreads, in composition units.</summary>
    [Display(Name = nameof(GraphicsStrings.Radius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Radius { get; } = Property.CreateAnimatable(12f);

    [Display(Name = nameof(GraphicsStrings.Bokeh_Shape), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<BokehShape> Shape { get; } = Property.CreateExpression(BokehShape.Circle);

    [Display(Name = nameof(GraphicsStrings.Bokeh_Sides), ResourceType = typeof(GraphicsStrings))]
    [Range(3, 12)]
    public IExpressionProperty<int> Sides { get; } = Property.CreateAnimatable(6);

    /// <summary>Clockwise, in degrees. At zero, a corner of the polygon points up.</summary>
    [Display(Name = nameof(GraphicsStrings.Bokeh_Rotation), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Rotation { get; } = Property.CreateAnimatable(0f);

    /// <summary>How much brighter the bright parts get before they spread, in percent.</summary>
    [Display(Name = nameof(GraphicsStrings.Bokeh_Highlight), Description = nameof(GraphicsStrings.Bokeh_Highlight_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Highlight { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Bokeh_Quality), Description = nameof(GraphicsStrings.Bokeh_Quality_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<BokehQuality> Quality { get; } = Property.CreateExpression(BokehQuality.Medium);

    private readonly record struct EffectData(float Radius, BokehShape Shape, int Sides, float Rotation, float Highlight, BokehQuality Quality)
    {
        public int Rings => Quality switch { BokehQuality.Low => 3, BokehQuality.High => 8, _ => 5 };

        // Rings around the middle, the last one on the outline of the shape. X and Y are where a sample is for a radius
        // of 1, and Z is its weight: its share of the area around its ring, half of which lies outside for the last one.
        public Vector4[] CreateSamples(int rings)
        {
            var samples = new List<Vector4> { new(0, 0, 1 / 3f, 0) };
            float corner = Rotation * (MathF.PI / 180) - MathF.PI / 2;
            float sector = MathF.Tau / Sides;
            for (int ring = 1; ring <= rings; ring++)
            {
                float distance = ring / (float)rings;
                // About six more on each ring keeps them about as far apart along a ring as across the rings.
                int perSide = Shape == BokehShape.Polygon ? Math.Max(1, (int)MathF.Round(6f * ring / Sides)) : 6 * ring;
                int sides = Shape == BokehShape.Polygon ? Sides : 1;
                int count = sides * perSide;
                float weight = (ring == rings ? ring : 2 * ring) / (float)count;
                for (int side = 0; side < sides; side++)
                {
                    float angle = corner + side * sector;
                    var from = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    var to = new Vector2(MathF.Cos(angle + sector), MathF.Sin(angle + sector));
                    for (int i = 0; i < perSide; i++)
                    {
                        float along = i / (float)perSide;
                        Vector2 position = Shape == BokehShape.Polygon
                            ? Vector2.Lerp(from, to, along)
                            : new Vector2(MathF.Cos(corner + along * MathF.Tau), MathF.Sin(corner + along * MathF.Tau));
                        position *= distance;
                        samples.Add(new Vector4(position, weight, 0));
                    }
                }
            }

            return [.. samples];
        }
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            // Interpolation and detached resources can bypass the editor's ranges.
            if (!float.IsFinite(Radius) || Radius <= 0 || !float.IsFinite(Rotation) || !float.IsFinite(Highlight))
                return;

            var data = new EffectData(Radius, Shape, Math.Clamp(Sides, 3, 12), Rotation, Math.Max(Highlight, 0) / 100, Quality);
            context.ShaderGraph(data, static (graph, values) =>
            {
                ShaderBuffer shapeSamples = graph.Buffer<Vector4>("shape-samples", values.CreateSamples(values.Rings));
                ShaderBuffer fillSamples = graph.Buffer<Vector4>("fill-samples", values.CreateSamples(FillRings));
                ShaderImage current = graph.Source;
                if (values.Highlight > 0)
                {
                    ShaderImage highlights = graph.Image2D("highlights", graph.Source);
                    graph.Fullscreen("highlights", s_highlights, pass =>
                    {
                        pass.SampledImage(0, graph.Source);
                        pass.ImageParameters(1, graph.Source);
                        pass.ImageParameters(2, highlights);
                        pass.PushConstants(values.Highlight);
                        pass.InputRegion(0, ShaderInputRegion.Identity);
                        pass.ColorAttachment(0, highlights);
                    });
                    current = highlights;
                }

                // A large shape loses nothing to a coarser grid, and the cost stops growing with the size.
                for (int level = 1; values.Radius * current.ResolutionScale > 32; level++)
                {
                    ShaderImage source = current;
                    current = graph.Image2D($"downsample-{level}", source, resolutionScale: source.ResolutionScale / 2);
                    LightEffectPasses.Downsample(graph, source, current);
                }

                // The first round draws the shape, as points as far apart as its rings. Two thirds of that distance fills
                // the gaps evenly, and with the same shape instead of a disc it keeps the corners. Each later round does the
                // same for the one before it with fewer samples, until the gaps are less than a pixel. The radii after the
                // first add up to one distance between rings, which is how far past the shape its soft edge can reach.
                float ringDistance = values.Radius / values.Rings;
                current = Round("shape", shapeSamples, values.Radius, ringDistance + values.Radius);
                int round = 1;
                for (float fill = ringDistance * FillRatio; fill * current.ResolutionScale >= 1; fill *= FillRatio / FillRings)
                    current = Round($"fill-{round++}", fillSamples, fill, 0);

                if (current.ResolutionScale == 1)
                    return graph.Output(current);

                ShaderImage reduced = current;
                ShaderImage output = graph.Image2D("output", reduced);
                graph.Fullscreen("output", s_output, pass =>
                {
                    pass.SampledImage(0, reduced);
                    pass.ImageParameters(1, reduced);
                    pass.ImageParameters(2, output);
                    pass.InputRegion(0, ShaderInputRegion.Identity);
                    pass.ColorAttachment(0, output);
                });
                return graph.Output(output);

                ShaderImage Round(string name, ShaderBuffer samples, float radius, float padding)
                {
                    ShaderImage source = current;
                    ShaderImage destination = graph.Image2D(name, source, padding: padding, resolutionScale: source.ResolutionScale);
                    graph.Fullscreen(name, s_round, pass =>
                    {
                        pass.SampledImage(0, source);
                        pass.ImageParameters(1, source);
                        pass.ImageParameters(2, destination);
                        pass.StorageBuffer(3, samples, ResourceAccess.Read);
                        pass.PushConstants(radius);
                        pass.InputRegion(0, new(Matrix.Identity, radius));
                        pass.ColorAttachment(0, destination);
                    });
                    return destination;
                }
            });
        }
    }

    private static readonly ShaderProgram s_highlights = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float amount; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = texture(image, (position - src.raster.xy) / src.raster.zw);
            float luminance = color.a > 0 ? dot(color.rgb / color.a, vec3(0.2126, 0.7152, 0.0722)) : 0;
            color.rgb *= 1 + pc.amount * smoothstep(0.5, 1.0, luminance);
        }
        """));

    private static readonly ShaderProgram s_round = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(std430, binding = 3) readonly buffer Samples { vec4 values[]; } samples;
        layout(push_constant) uniform Values { float radius; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = vec4(0);
            float total = 0;
            for (int i = 0; i < samples.values.length(); i++) {
                vec4 s = samples.values[i];
                color += texture(image, (position + s.xy * pc.radius - src.raster.xy) / src.raster.zw) * s.z;
                total += s.z;
            }
            color /= total;
        }
        """));

    private static readonly ShaderProgram s_output = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = texture(image, (position - src.raster.xy) / src.raster.zw);
        }
        """));
}

using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Extracts highlights, blurs them in two directions, and composites a linear-light glow.</summary>
[Display(Name = nameof(GraphicsStrings.Bloom), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Bloom : FilterEffect
{
    public Bloom() => ScanProperties<Bloom>();

    /// <summary>The glow's sampling radius in composition units.</summary>
    [Display(Name = nameof(GraphicsStrings.Radius), Description = nameof(GraphicsStrings.Bloom_Radius_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Radius { get; } = Property.CreateAnimatable(12f);

    /// <summary>The straight, linear luminance at the center of the highlight threshold.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_Threshold), Description = nameof(GraphicsStrings.Bloom_Threshold_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>The transition's half-width as a percentage of Threshold. Zero preserves the hard threshold.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_SoftKnee), Description = nameof(GraphicsStrings.Bloom_SoftKnee_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> SoftKnee { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Bloom_Intensity), Description = nameof(GraphicsStrings.Bloom_Intensity_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>Masks the glow with the original alpha instead of extending the image silhouette.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_PreserveAlpha), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> PreserveAlpha { get; } = Property.CreateAnimatable(false);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Radius) || Radius < 0 || !float.IsFinite(Threshold) || Threshold < 0
                || !float.IsFinite(SoftKnee) || !float.IsFinite(Intensity) || Intensity <= 0)
                return;

            context.ShaderGraph((Radius, Threshold, SoftKnee: Math.Clamp(SoftKnee, 0, 100) / 100,
                    Intensity, PreserveAlpha),
                static (graph, values) =>
                {
                    ShaderImage bright = graph.Image2D("bright", graph.Source, padding: values.Radius);
                    ShaderImage mask = graph.Image2D("mask", bright, TextureFormat.R16Float);
                    ShaderImage output = graph.Image2D("output", graph.Source, padding: values.PreserveAlpha ? 0 : values.Radius);
                    LightEffectPasses.ExtractHighlights(graph, graph.Source, bright, mask, values.Threshold, values.SoftKnee);
                    ShaderImage glow = LightEffectPasses.GaussianBlur(graph, bright, values.Radius);
                    LightEffectPasses.Composite(graph, graph.Source, glow, mask, output, values.Intensity, values.PreserveAlpha);
                    return graph.Output(output);
                });
        }
    }
}

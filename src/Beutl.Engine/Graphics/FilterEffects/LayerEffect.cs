using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LayerEffect), ResourceType = typeof(GraphicsStrings))]
public partial class LayerEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        // Legacy LayerEffect merges the whole operation set into one SrcOver-composited buffer at their union bounds,
        // each drawn at its own offset — exactly the fan-in composite primitive (no per-branch offsets: an operation
        // renders at its own bounds).
        builder.Composite(CompositeNodeDescriptor.Create(BlendMode.SrcOver, structuralToken: nameof(LayerEffect)));
    }
}

using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

public sealed partial class RenderNodeContext
{
    /// <summary>Records a declarative graph over each image in the input stream.</summary>
    public RenderFragmentHandle ShaderGraph(RenderFragmentHandle input, ShaderGraphDescription description)
        => ShaderGraph(input, description, null);

    internal RenderFragmentHandle ShaderGraph(RenderFragmentHandle input, ShaderGraphDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        ArgumentNullException.ThrowIfNull(description);
        var transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        Rect bounds = description.TransformBounds(reference.Bounds);
        EffectiveScale scale = ResolveMaterializingScale(reference, bounds, workingScalePolicy);
        scale = EffectiveScale.At(description.ClampWorkingScale(reference.Bounds, scale.Value, default));
        return transaction.CreateFragment(RenderFragmentKind.ShaderGraph, bounds, scale,
            reference.ValueCardinality, reference.ContributesValuesToTarget, canBeUsedAsValueInput: true,
            reference.HasTargetEffects, reference.HasOpaqueExternalWork, [reference],
            new ShaderGraphRenderFragmentPayload(description, workingScalePolicy), RenderFragmentHitTest.Bounds);
    }
}

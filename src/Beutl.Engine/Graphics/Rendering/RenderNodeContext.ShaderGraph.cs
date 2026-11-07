using System.Collections.Immutable;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

public sealed partial class RenderNodeContext
{
    /// <summary>Records a declarative graph over each image in the input stream.</summary>
    public RenderFragmentHandle ShaderGraph(RenderFragmentHandle input, ShaderGraphDescription description)
        => ShaderGraph(input, description, (FilterEffectWorkingScalePolicy?)null);

    /// <summary>Records a graph over the input, with the pictures it declared besides the source.</summary>
    /// <param name="externalImages">One value-eligible fragment per <see cref="ShaderGraphBuilder.ExternalImage"/>, in that order.</param>
    internal RenderFragmentHandle ShaderGraph(RenderFragmentHandle input, ShaderGraphDescription description,
        IReadOnlyList<RenderFragmentHandle> externalImages)
    {
        ArgumentNullException.ThrowIfNull(externalImages);
        return ShaderGraph(input, description, null, externalImages);
    }

    internal RenderFragmentHandle ShaderGraph(RenderFragmentHandle input, ShaderGraphDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy, IReadOnlyList<RenderFragmentHandle>? externalImages = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        var transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        ImmutableArray<RenderFragmentReference> externals = externalImages == null
            ? []
            : transaction.GetReferences(externalImages, nameof(externalImages));
        if (externals.Length != description.ExternalImageCount)
            throw new ArgumentException($"The graph declares {description.ExternalImageCount} external images; {externals.Length} were supplied.", nameof(externalImages));
        foreach (RenderFragmentReference external in externals)
            EnsureValueInput(external, nameof(externalImages));
        Rect bounds = description.TransformBounds(reference.Bounds);
        ImmutableArray<RenderFragmentReference> inputs = [reference, .. externals];
        // The region analysis resolves the density over all of the inputs, so the plan reads them too: a picture denser
        // than the source raises the density the graph runs at.
        EffectiveScale scale = ShaderGraphRenderFragmentPayload.ResolvePlannedScale(
            description, workingScalePolicy, inputs, OutputScale, MaxWorkingScale);
        return transaction.CreateFragment(RenderFragmentKind.ShaderGraph, bounds, scale,
            reference.ValueCardinality, reference.ContributesValuesToTarget, canBeUsedAsValueInput: true,
            reference.HasTargetEffects || externals.Any(static external => external.HasTargetEffects),
            reference.HasOpaqueExternalWork || externals.Any(static external => external.HasOpaqueExternalWork),
            inputs,
            new ShaderGraphRenderFragmentPayload(description, workingScalePolicy), RenderFragmentHitTest.Bounds);
    }
}

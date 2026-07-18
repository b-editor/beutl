using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>
/// A custom-render-node node (feature 004, data-model §1): the declarative home for an effect whose execution
/// lives in a custom <see cref="FilterEffectRenderNode"/> rather than in describable descriptors — the
/// <see cref="NodeGraphFilterEffect"/> being the canonical case. Embedding it in a graph (a
/// <see cref="FilterEffectGroup"/>, a <see cref="DelayAnimationEffect"/> branch, any container that walks its
/// children through <see cref="FilterEffect.Describe"/>) used to be impossible: such an effect threw from
/// <c>Describe</c> because its only path was its own top-level render node. This descriptor gives the executor a
/// first-class way to run that render node as one node inside the plan — it materializes the current ops as the
/// child node's input, drives the resource's captured render-node factory, and threads the
/// returned ops onward. Never fused; its full-frame contract prevents ROI cropping while the child lays itself out.
/// </summary>
public sealed record CustomRenderNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.CustomRenderNode;

    private CustomRenderNodeDescriptor(
        FilterEffect.Resource resource, FilterEffectRenderNodeFactory factory)
    {
        Resource = resource;
        Factory = factory;
    }

    /// <summary>The child effect resource whose render node runs this node. Its reference identity is structural; its
    /// <see cref="Beutl.Engine.EngineObject.Resource.Version"/> rebinds per frame (a swap or type change recompiles).</summary>
    public FilterEffect.Resource Resource { get; }

    /// <summary>The child's <see cref="FilterEffectRenderNodeFactory.NodeType"/>, part of the structural identity.</summary>
    public Type NodeType => Factory.NodeType;

    internal FilterEffectRenderNodeFactory Factory { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds => BoundsContract.FullFrame;

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>Builds a custom-render-node descriptor for <paramref name="resource"/>, capturing its render-node type.</summary>
    public static CustomRenderNodeDescriptor Create(CustomRenderNodeFilterEffect.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        FilterEffectRenderNodeFactory factory = resource.RenderNodeFactory
            ?? throw new InvalidOperationException("A custom filter effect returned a null render-node factory.");
        return Create(resource, factory);
    }

    internal static CustomRenderNodeDescriptor Create(
        FilterEffect.Resource resource, FilterEffectRenderNodeFactory factory)
        => new(resource, factory);
}

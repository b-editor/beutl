using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Captures a filter effect's render-node type together with its constructor as one value, so the render-graph diff
/// and the node it actually creates can never disagree (feature 004). An effect that needs a custom
/// <see cref="FilterEffectRenderNode"/> (e.g. a non-supply working scale, 003/FR-036) overrides
/// <see cref="FilterEffect.Resource.RenderNodeFactory"/> once; the diff reuses the effect's node across drawable
/// re-renders only while the existing node's runtime type equals <see cref="NodeType"/>, and that reuse is what
/// keeps the node's plan and prefix caches alive on animated frames. Pairing the type and the constructor here
/// removes the earlier drift hazard where overriding one of two members and forgetting the other silently
/// recompiled the plan every frame.
/// </summary>
public readonly struct FilterEffectRenderNodeFactory
{
    private readonly Func<FilterEffect.Resource, FilterEffectRenderNode>? _create;

    private FilterEffectRenderNodeFactory(Type nodeType, Func<FilterEffect.Resource, FilterEffectRenderNode> create)
    {
        NodeType = nodeType;
        _create = create;
    }

    /// <summary>The concrete <see cref="FilterEffectRenderNode"/> type <see cref="Create"/> instantiates.</summary>
    public Type NodeType { get; }

    /// <summary>
    /// Builds a factory for node type <typeparamref name="TNode"/> from its constructor. The captured type is
    /// <c>typeof(TNode)</c>, so it always matches the node <paramref name="create"/> produces.
    /// </summary>
    public static FilterEffectRenderNodeFactory Of<TNode>(Func<FilterEffect.Resource, TNode> create)
        where TNode : FilterEffectRenderNode
    {
        ArgumentNullException.ThrowIfNull(create);
        return new FilterEffectRenderNodeFactory(typeof(TNode), create);
    }

    /// <summary>Instantiates the render node for <paramref name="resource"/>.</summary>
    public FilterEffectRenderNode Create(FilterEffect.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_create is null)
        {
            throw new InvalidOperationException(
                $"This {nameof(FilterEffectRenderNodeFactory)} was default-constructed; build one with {nameof(Of)}.");
        }

        return _create(resource);
    }

    // The captured constructor, handed straight to PushNode so the recording path allocates no per-call delegate.
    internal Func<FilterEffect.Resource, FilterEffectRenderNode> NodeConstructor => _create!;
}

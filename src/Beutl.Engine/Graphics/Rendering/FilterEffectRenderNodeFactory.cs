using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Captures a filter effect's render-node type together with its constructor as one value, and rejects a constructor
/// whose result has a different exact runtime type (feature 004). An effect that needs a custom
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
    /// Builds a factory declaring node type <typeparamref name="TNode"/> from its constructor. The captured type is
    /// <c>typeof(TNode)</c>; <see cref="Create"/> verifies that the constructor returns that exact type, so callers
    /// must use the concrete node type rather than a broader base type.
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

        FilterEffectRenderNode node = _create(resource);
        if (node.GetType() != NodeType)
        {
            node.Dispose();
            throw new InvalidOperationException(
                $"The render-node factory declared '{NodeType.FullName}' but created "
                + $"'{node.GetType().FullName}'. Use Of<TNode> with the concrete node type.");
        }

        return node;
    }

}

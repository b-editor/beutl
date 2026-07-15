using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Captures a fully opaque filter effect's resource type, render-node type, and constructor as one value, and rejects
/// a constructor whose result has a different exact runtime type (feature 004). A
/// <see cref="CustomRenderNodeFilterEffect.Resource"/> returns one retained factory instance; the diff reuses the
/// effect's node across drawable re-renders only while it was created by that same instance, and that reuse is what
/// keeps the node's plan and prefix caches alive on animated frames. Pairing the type and the constructor here
/// removes the earlier drift hazard where overriding one of two members and forgetting the other silently
/// recreated the node every frame. Standard compiled-plan customization uses
/// <see cref="PlanFilterEffectRenderNodeFactory"/> instead.
/// </summary>
public sealed class FilterEffectRenderNodeFactory
{
    private readonly Func<FilterEffect.Resource, FilterEffectRenderNode> _create;

    private FilterEffectRenderNodeFactory(
        Type resourceType, Type nodeType, Func<FilterEffect.Resource, FilterEffectRenderNode> create)
    {
        ResourceType = resourceType;
        NodeType = nodeType;
        _create = create;
    }

    /// <summary>The resource type accepted by the captured constructor.</summary>
    public Type ResourceType { get; }

    /// <summary>The concrete <see cref="FilterEffectRenderNode"/> type <see cref="Create"/> instantiates.</summary>
    public Type NodeType { get; }

    /// <summary>
    /// Builds a factory whose constructor accepts the concrete resource type <typeparamref name="TResource"/> and
    /// creates the concrete node type <typeparamref name="TNode"/>. Both types are retained and checked before the
    /// plugin callback runs, eliminating per-call casts and giving a mismatched resource a deterministic diagnostic.
    /// </summary>
    public static FilterEffectRenderNodeFactory Of<TResource, TNode>(Func<TResource, TNode> create)
        where TResource : FilterEffect.Resource
        where TNode : FilterEffectRenderNode
    {
        ArgumentNullException.ThrowIfNull(create);
        return new FilterEffectRenderNodeFactory(
            typeof(TResource), typeof(TNode),
            resource => create((TResource)resource));
    }

    /// <summary>Instantiates the render node for <paramref name="resource"/>.</summary>
    public FilterEffectRenderNode Create(FilterEffect.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ResourceType.IsInstanceOfType(resource))
        {
            throw new ArgumentException(
                $"The render-node factory requires resource type '{ResourceType.FullName}' but received "
                + $"'{resource.GetType().FullName}'.", nameof(resource));
        }

        FilterEffectRenderNode? node = _create(resource);
        if (node is null)
            throw new InvalidOperationException("The render-node factory returned null.");
        if (node.GetType() != NodeType)
        {
            node.Dispose();
            throw new InvalidOperationException(
                $"The render-node factory declared '{NodeType.FullName}' but created "
                + $"'{node.GetType().FullName}'. Use Of<TResource, TNode> with the concrete node type.");
        }

        node.CreationFactory = this;
        return node;
    }

    internal bool Matches(FilterEffectRenderNode node)
        => ReferenceEquals(node.CreationFactory, this);
}

/// <summary>
/// Creates a standard compiled-plan render node with a narrowly overridable execution policy. This route retains
/// the engine's graph compiler, ROI propagation, pooling, and caches; use
/// <see cref="FilterEffectRenderNodeFactory"/> on <see cref="CustomRenderNodeFilterEffect.Resource"/> only for a
/// fully opaque execution implementation.
/// </summary>
public sealed class PlanFilterEffectRenderNodeFactory
{
    private PlanFilterEffectRenderNodeFactory(FilterEffectRenderNodeFactory inner)
    {
        Inner = inner;
    }

    internal FilterEffectRenderNodeFactory Inner { get; }

    /// <summary>Creates a plan-node factory for a concrete resource and plan-node subclass.</summary>
    public static PlanFilterEffectRenderNodeFactory Of<TResource, TNode>(Func<TResource, TNode> create)
        where TResource : FilterEffect.Resource
        where TNode : PlanFilterEffectRenderNode
    {
        ArgumentNullException.ThrowIfNull(create);
        return new PlanFilterEffectRenderNodeFactory(
            FilterEffectRenderNodeFactory.Of<TResource, TNode>(create));
    }
}

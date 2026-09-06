namespace Beutl.Graphics.Rendering;

/// <summary>
/// Records the complete ordered set of roots for one target before any of them execute. The roots remain
/// externally owned; this request-local facade never retains fragment handles or disposes render nodes.
/// </summary>
internal sealed class CompleteTargetRenderNode : RenderNode
{
    private readonly RenderNode _first;

    // Replaced rather than mutated, so a span handed out by ChildNodes survives an UpdateRoots mid-traversal.
    private RenderNode[] _roots;

    public CompleteTargetRenderNode(RenderNode first, IEnumerable<RenderNode> remaining)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(remaining);
        _first = first;
        _roots = [first, .. remaining];
        if (_roots.Any(static root => root is null))
            throw new ArgumentException("A complete-target root sequence cannot contain null nodes.", nameof(remaining));
    }

    public void UpdateRoots(IEnumerable<RenderNode> remaining)
    {
        ArgumentNullException.ThrowIfNull(remaining);
        RenderNode[] roots = [_first, .. remaining];
        if (roots.Any(static root => root is null))
            throw new ArgumentException("A complete-target root sequence cannot contain null nodes.", nameof(remaining));
        if (HasSameRoots(roots))
            return;

        _roots = roots;
        MarkChanged();
    }

    private bool HasSameRoots(RenderNode[] roots)
    {
        if (_roots.Length != roots.Length)
            return false;

        for (int index = 0; index < roots.Length; index++)
        {
            if (!ReferenceEquals(_roots[index], roots[index]))
                return false;
        }

        return true;
    }

    public override ReadOnlySpan<RenderNode> ChildNodes => _roots;

    public override void Process(RenderNodeContext context)
    {
        foreach (RenderNode root in _roots)
            context.PublishRange(context.RecordSubtree(root));
    }
}

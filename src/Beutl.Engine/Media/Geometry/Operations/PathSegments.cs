namespace Beutl.Media;

public sealed class PathSegments : AffectsRenders<PathSegment>
{
    public PathSegments(IModifiableHierarchical parent)
    {
        Parent = parent;
        Attached += item => Parent.AddChild(item);
        Detached += item => Parent.RemoveChild(item);
    }

    public PathSegments()
    {
    }

    public IModifiableHierarchical? Parent { get; }
}

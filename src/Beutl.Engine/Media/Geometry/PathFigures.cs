namespace Beutl.Media;

public sealed class PathFigures : AffectsRenders<PathFigure>
{
    public PathFigures(IModifiableHierarchical parent)
    {
        Parent = parent;
        Attached += item => Parent.AddChild(item);
        Detached += item => Parent.RemoveChild(item);
    }

    public PathFigures()
    {
    }

    public IModifiableHierarchical? Parent { get; }
}

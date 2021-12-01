namespace BEditorNext.Animation;

public abstract class Animator : ILogicalElement
{
    private ILogicalElement? _parent;

    public ILogicalElement? LogicalParent
    {
        get => _parent;
        set => _parent = value;
    }

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();
}

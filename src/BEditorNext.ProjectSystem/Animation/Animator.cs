namespace BEditorNext.Animation;

public abstract class Animator : ILogicalElement
{
    public ILogicalElement? LogicalParent { get; set; }

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();
}

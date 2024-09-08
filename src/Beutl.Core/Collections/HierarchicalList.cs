namespace Beutl.Collections;

public class HierarchicalList<T> : CoreList<T>
    where T : IHierarchical
{
    public HierarchicalList(IModifiableHierarchical parent)
    {
        ResetBehavior = ResetBehavior.Remove;
        Parent = parent;
        Attached += item => parent.AddChild(item);
        Detached += item => parent.RemoveChild(item);
    }

    public HierarchicalList()
    {
        ResetBehavior = ResetBehavior.Remove;
    }

    public IModifiableHierarchical? Parent { get; }
}

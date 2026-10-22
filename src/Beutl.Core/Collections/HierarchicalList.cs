namespace Beutl.Collections;

public class HierarchicalList<T> : CoreList<T>
    where T : IHierarchical
{
    public HierarchicalList(IModifiableHierarchical parent)
    {
        ResetBehavior = ResetBehavior.Remove;
        Parent = parent;
        WriteOwner = parent;
        Attached += item => parent.AddChild(item);
        Detached += item => parent.RemoveChild(item);
    }

    public HierarchicalList()
    {
        ResetBehavior = ResetBehavior.Remove;
    }

    public IModifiableHierarchical? Parent { get; }

    // An item that holds the list's parent would become its own ancestor.
    private protected override void VerifyInsert(T item)
    {
        for (IHierarchical? owner = Parent; owner != null; owner = owner.HierarchicalParent)
        {
            if (ReferenceEquals(owner, item))
                throw new InvalidOperationException("An object cannot be added to a list inside it.");
        }
    }
}

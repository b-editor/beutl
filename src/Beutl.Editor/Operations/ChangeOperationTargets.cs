namespace Beutl.Editor.Operations;

// What an operation changes, as the saves that write only what changed place it: the object it edits, and the items a
// collection edit adds, removes or moves. The target is null for an operation that names none, and for an easing curve
// without a parent.
internal static class ChangeOperationTargets
{
    public static (object? Target, IEnumerable<object?> Items) Find(ChangeOperation operation)
    {
        switch (operation)
        {
            case IUpdatePropertyValueOperation update:
                return (update.Object, []);
            case ICollectionChangeOperation collection:
                return (collection.Object, collection.Items);
            case UpdateNodeMemberOperation member:
                return (member.NodeMember, []);
            case UpdateSplineEasingOperation easing:
                return (easing.Parent, []);
            default:
                return (null, []);
        }
    }
}

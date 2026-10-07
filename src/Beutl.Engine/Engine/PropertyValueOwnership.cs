namespace Beutl.Engine;

internal static class PropertyValueOwnership
{
    // A replaced value leaves the owner's hierarchy and its replacement joins it; values that are not
    // hierarchical, and owners that cannot be modified, are left alone.
    public static void Reparent<TValue>(EngineObject? owner, TValue oldValue, TValue newValue)
    {
        if (owner is IModifiableHierarchical ownerHierarchical)
        {
            if (oldValue is IHierarchical oldHierarchical)
                ownerHierarchical.RemoveChild(oldHierarchical);

            if (newValue is IHierarchical newHierarchical)
                ownerHierarchical.AddChild(newHierarchical);
        }
    }
}

namespace Beutl.Engine;

// A value that a group of values of its own kind can take the place of, as a transform, a filter effect or an audio
// effect can.
internal static class EngineObjectGroups
{
    // The group the value is, or a new one that takes the value's place and holds it; created says which.
    public static TGroup GetOrCreateGroup<T, TGroup>(
        T? current, Action<TGroup> replace, Func<TGroup, IListProperty<T>> childrenOf, out bool created)
        where T : EngineObject
        where TGroup : T, new()
    {
        if (current is TGroup existingGroup)
        {
            created = false;
            return existingGroup;
        }

        var group = new TGroup();
        // The group takes the value's place first. Added while the property still owned it, the value would stay in
        // the group's children but lose its parent when the property released it.
        replace(group);
        if (current != null)
        {
            childrenOf(group).Add(current);
        }

        created = true;
        return group;
    }
}

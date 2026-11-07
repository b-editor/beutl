using System.Collections.Immutable;

namespace Beutl.ProjectSystem;

// How a scene and an element set save their groups, each group as its element ids joined by ':', and prune them.
internal static class ElementGroups
{
    // Prunes ids from every group and disbands any group left with fewer than two members. Returns true if any group
    // changed.
    public static bool RemoveIds(IListProperty<ImmutableHashSet<Guid>> groups, IReadOnlyCollection<Guid> ids)
    {
        // Look the members up in a set: Overlaps and Except walk their argument, which costs groups × ids.
        var idSet = new HashSet<Guid>(ids);
        bool removed = false;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            ImmutableHashSet<Guid> group = groups[i];
            if (!group.Any(idSet.Contains)) continue;

            ImmutableHashSet<Guid> updated = group.Except(group.Where(idSet.Contains));
            if (updated.Count >= 2)
            {
                groups[i] = updated;
            }
            else
            {
                groups.RemoveAt(i);
            }

            removed = true;
        }

        return removed;
    }

    public static string[] Write(IEnumerable<ImmutableHashSet<Guid>> groups)
        => groups.Select(ids => string.Join(':', ids)).ToArray();

    // A group keeps only the ids in elementIds, after mapId, and is dropped once fewer than two are left. Text that
    // is not an id reads as Guid.Empty.
    public static IEnumerable<ImmutableHashSet<Guid>> Read(
        string[]? groups, IReadOnlySet<Guid> elementIds, Func<Guid, Guid>? mapId = null)
    {
        foreach (string group in groups ?? [])
        {
            ImmutableHashSet<Guid> ids = group.Split(':')
                .Select(text => Guid.TryParse(text, out Guid id) ? id : Guid.Empty)
                .Select(id => mapId?.Invoke(id) ?? id)
                .Where(elementIds.Contains)
                .ToImmutableHashSet();
            if (ids.Count >= 2)
            {
                yield return ids;
            }
        }
    }
}

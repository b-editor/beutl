using System.Collections.Immutable;

namespace Beutl.ProjectSystem;

// How a scene and an element set save their groups: each group as its element ids joined by ':'.
internal static class ElementGroups
{
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

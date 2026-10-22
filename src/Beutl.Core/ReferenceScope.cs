namespace Beutl;

// Ids are unique per project, yet a copied file can hold the same ones twice: a reference
// resolves within its holder's scene first, then anywhere in the project (#2885).
internal static class ReferenceScope
{
    // The object id names for anchor; root defaults to anchor's hierarchical root.
    public static ICoreObject? Find(IHierarchical anchor, Guid id, ICoreObject? root = null)
    {
        root ??= anchor.FindHierarchicalRoot() as ICoreObject;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (anchor.FindHierarchicalParent<ProjectItem>(includeSelf: true) is { } item
            && Find(item, id, visited) is { } inItem)
        {
            return inItem;
        }

        return root is null ? null : Find(root, id, visited);
    }

    // Skips what visited already holds, so the second search does not walk the scene again.
    private static ICoreObject? Find(ICoreObject scope, Guid id, HashSet<object> visited)
        => scope.Find(obj => (obj as ICoreObject)?.Id == id, includeSelf: true, visited) as ICoreObject;
}

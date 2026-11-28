namespace Beutl;

// Ids are unique per project, yet a copied file can hold the same ones twice: a reference
// resolves within its holder's scene first, then anywhere in the project (#2885). The application
// can hold several projects, even two copies of one with the same ids, so the search never leaves
// the holder's project.
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

        ICoreObject? scope = SearchScope(anchor, root);
        return scope is null ? null : Find(scope, id, visited);
    }

    // The project that holds anchor when root contains it, else root: an object outside a project, or a
    // search without a root, behaves as before the application held more than one project.
    public static ICoreObject? SearchScope(IHierarchical anchor, ICoreObject? root)
    {
        if (root is null || anchor.FindHierarchicalParent<Project>(includeSelf: true) is not { } project)
            return root;

        for (IHierarchical? current = project; current is not null; current = current.HierarchicalParent)
        {
            if (ReferenceEquals(current, root))
                return project;
        }

        return root;
    }

    // Skips what visited already holds, so the second search does not walk the scene again.
    private static ICoreObject? Find(ICoreObject scope, Guid id, HashSet<object> visited)
        => scope.Find(obj => (obj as ICoreObject)?.Id.Value == id, includeSelf: true, visited) as ICoreObject;
}

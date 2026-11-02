namespace Beutl.AgentToolkit.Reconciliation;

// The objects of a tree by Id, from one walk in the order FindById searches, so a repeated Id finds its first object.
// FindById walks the tree on every call, which a plan looking up each object of the document cannot afford.
// A plan only reads the trees it looks up, so the walks stay valid for as long as the plan lasts.
internal sealed class ObjectLookup(CoreObject root)
{
    private Dictionary<Guid, ICoreObject>? _objects;
    private Dictionary<Guid, ICoreObject>? _projectItemObjects;
    private Dictionary<Guid, ICoreObject>? _projectObjects;

    public CoreObject Root { get; } = root;

    public ICoreObject? Find(Guid id) => (_objects ??= Walk(Root)).GetValueOrDefault(id);

    // What a reference held in this tree resolves to, in the order of ReferenceScope.Find: within its project item
    // first, then anywhere in the project.
    public ICoreObject? FindReference(Guid id)
    {
        if (Root is not IHierarchical anchor)
        {
            return Find(id);
        }

        _projectItemObjects ??= anchor.FindHierarchicalParent<ProjectItem>(includeSelf: true) is { } item ? Walk(item) : [];
        if (_projectItemObjects.TryGetValue(id, out ICoreObject? inItem))
        {
            return inItem;
        }

        _projectObjects ??= Walk(anchor.FindHierarchicalRoot() as ICoreObject ?? Root);
        return _projectObjects.GetValueOrDefault(id);
    }

    private static Dictionary<Guid, ICoreObject> Walk(ICoreObject root)
    {
        var objects = new Dictionary<Guid, ICoreObject>();
        foreach (object item in ChildObjects.Walk(root))
        {
            if (item is ICoreObject coreObject)
            {
                objects.TryAdd(coreObject.Id.Value, coreObject);
            }
        }

        return objects;
    }
}

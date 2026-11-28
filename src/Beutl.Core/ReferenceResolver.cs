namespace Beutl;

public class ReferenceResolver(IHierarchical anchor, Guid id)
{
    public async Task<ICoreObject> Resolve()
    {
        var root = anchor.HierarchicalRoot;
        if (root == null)
        {
            var tcs = new TaskCompletionSource<IHierarchicalRoot>();
            void OnAttachedToHierarchy(object? sender, HierarchyAttachmentEventArgs e)
            {
                anchor.AttachedToHierarchy -= OnAttachedToHierarchy;
                tcs.SetResult(e.Root);
            }

            anchor.AttachedToHierarchy += OnAttachedToHierarchy;
            root = await tcs.Task;
        }

        var found = ReferenceScope.Find(anchor, id, root as ICoreObject);
        if (found != null)
        {
            return found;
        }
        else
        {
            // Only an object that joins the anchor's project resolves it, not one of another loaded project.
            Project? project = anchor.FindHierarchicalParent<Project>(includeSelf: true);
            var tcs = new TaskCompletionSource<ICoreObject>();
            void OnDescendantAttached(object? sender, IHierarchical e)
            {
                if (e is ICoreObject coreObject && coreObject.Id.Value == id
                    && (project is null || ReferenceEquals(e.FindHierarchicalParent<Project>(includeSelf: true), project)))
                {
                    root.DescendantAttached -= OnDescendantAttached;
                    tcs.SetResult(coreObject);
                }
            }

            root.DescendantAttached += OnDescendantAttached;
            return await tcs.Task;
        }
    }
}

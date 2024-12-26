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

        var found = (root as ICoreObject)?.FindById(id);
        if (found != null)
        {
            return found;
        }
        else
        {
            var tcs = new TaskCompletionSource<ICoreObject>();
            void OnDescendantAttached(object? sender, IHierarchical e)
            {
                if (e is ICoreObject coreObject && coreObject.Id == id)
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

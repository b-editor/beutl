using System.Diagnostics.CodeAnalysis;

using BeUtl.Framework;
using BeUtl.ProjectSystem;

namespace BeUtl.Services;

public sealed class WorkspaceItemContainer : IWorkspaceItemContainer
{
    private readonly List<WeakReference<IWorkspaceItem>> _items = new();

    public bool IsCreated(string file)
    {
        return _items.Any(i => i.TryGetTarget(out IWorkspaceItem? target) && target.FileName == file);
    }

    public bool Remove(string file)
    {
        WeakReference<IWorkspaceItem>? item
            = _items.Find(i => i.TryGetTarget(out IWorkspaceItem? target) && target.FileName == file);
        if (item != null)
            return _items.Remove(item);
        else
            return false;
    }

    public bool TryGetOrCreateItem<T>(string file, [NotNullWhen(true)] out T? item)
        where T : class, IWorkspaceItem
    {
        item = default;
        item = _items.Find(i => i.TryGetTarget(out IWorkspaceItem? target) && target.FileName == file && target is T)
            ?.TryGetTarget(out IWorkspaceItem? target) ?? false
            ? target as T
            : null;
        if (item != null)
        {
            return true;
        }

        foreach (WorkspaceItemExtension ext in PackageManager.Instance.ExtensionProvider.MatchWorkspaceItemExtensions(file))
        {
            if (ext.TryCreateItem(file, out IWorkspaceItem? result) && result is T typed)
            {
                AddItem(typed);
                item = typed;
                return true;
            }
        }

        return false;
    }

    public bool TryGetOrCreateItem(string file, [NotNullWhen(true)] out IWorkspaceItem? item)
    {
        item = default;
        item = _items.Find(i => i.TryGetTarget(out IWorkspaceItem? target) && target.FileName == file)
            ?.TryGetTarget(out IWorkspaceItem? target) ?? false
            ? target
            : default;
        if (item != null)
        {
            return true;
        }

        foreach (WorkspaceItemExtension ext in PackageManager.Instance.ExtensionProvider.MatchWorkspaceItemExtensions(file))
        {
            if (ext.TryCreateItem(file, out IWorkspaceItem? result))
            {
                AddItem(result);
                item = result;
                return true;
            }
        }

        return false;
    }

    private void AddItem(IWorkspaceItem item)
    {
        foreach (WeakReference<IWorkspaceItem> wref in _items)
        {
            if (!wref.TryGetTarget(out _))
            {
                wref.SetTarget(item);
                return;
            }
        }

        _items.Add(new WeakReference<IWorkspaceItem>(item));
    }
}

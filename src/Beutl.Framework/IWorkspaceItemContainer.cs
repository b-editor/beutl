using System.Diagnostics.CodeAnalysis;

namespace Beutl.Framework;

public interface IWorkspaceItemContainer
{
    bool TryGetOrCreateItem<T>(string file, [NotNullWhen(true)] out T? item)
        where T : class, IWorkspaceItem;

    bool TryGetOrCreateItem(string file, [NotNullWhen(true)] out IWorkspaceItem? item);

    bool IsCreated(string file);

    bool Remove(string file);

    void Add(IWorkspaceItem item);
}

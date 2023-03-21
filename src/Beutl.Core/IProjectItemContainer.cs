using System.Diagnostics.CodeAnalysis;

namespace Beutl;

public interface IProjectItemContainer
{
    bool TryGetOrCreateItem<T>(string file, [NotNullWhen(true)] out T? item)
        where T : ProjectItem;

    bool TryGetOrCreateItem(string file, [NotNullWhen(true)] out ProjectItem? item);

    bool IsCreated(string file);

    bool Remove(string file);

    void Add(ProjectItem item);
}

using System.Diagnostics.CodeAnalysis;

namespace Beutl;

public interface IProjectItemGenerator
{
    bool TryCreateItem(
        string file,
        [NotNullWhen(true)] out ProjectItem? obj);

    bool TryCreateItem<T>(
        string file,
        [NotNullWhen(true)] out T? obj)
        where T : ProjectItem;
}

using System.Diagnostics.CodeAnalysis;

using Beutl.Api.Services;

namespace Beutl.Services;

public sealed class ProjectItemGenerator : IProjectItemGenerator
{
    public bool TryCreateItem(string file, [NotNullWhen(true)] out ProjectItem? obj)
    {
        obj = null;
        foreach (ProjectItemExtension ext in ExtensionProvider.Current.MatchProjectItemExtensions(file))
        {
            if (ext.TryCreateItem(file, out ProjectItem? result))
            {
                obj = result;
                return true;
            }
        }

        return false;
    }

    public bool TryCreateItem<T>(string file, [NotNullWhen(true)] out T? obj) where T : ProjectItem
    {
        obj = null;
        foreach (ProjectItemExtension ext in ExtensionProvider.Current.MatchProjectItemExtensions(file))
        {
            if (ext.TryCreateItem(file, out ProjectItem? result) && result is T typed)
            {
                obj = typed;
                return true;
            }
        }

        return false;
    }
}

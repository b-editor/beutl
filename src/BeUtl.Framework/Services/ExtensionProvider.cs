using System.Runtime.InteropServices;

namespace BeUtl.Framework.Services;

public sealed class ExtensionProvider
{
    internal readonly Dictionary<int, Extension[]> _allExtensions = new();

    public ExtensionProvider(PackageManager packageManager)
    {
        PackageManager = packageManager;

        foreach (Package package in CollectionsMarshal.AsSpan(PackageManager._loadedPackage))
        {
            _allExtensions.Add(package._id, package.GetExtensions().ToArray());
        }
    }

    public PackageManager PackageManager { get; }

    public IEnumerable<Extension> AllExtensions => _allExtensions.Values.SelectMany(ext => ext);

    public EditorExtension? MatchEditorExtension(string file)
    {
        foreach (Extension extension in AllExtensions)
        {
            if (extension is EditorExtension editorExtension &&
                editorExtension.IsSupported(file))
            {
                return editorExtension;
            }
        }

        return null;
    }

    public IEnumerable<EditorExtension> MatchEditorExtensions(string file)
    {
        foreach (Extension extension in AllExtensions)
        {
            if (extension is EditorExtension editorExtension &&
                editorExtension.IsSupported(file))
            {
                yield return editorExtension;
            }
        }
    }

    public WorkspaceItemExtension? MatchWorkspaceItemExtension(string file)
    {
        foreach (Extension extension in AllExtensions)
        {
            if (extension is WorkspaceItemExtension wsiExtension &&
                wsiExtension.IsSupported(file))
            {
                return wsiExtension;
            }
        }

        return null;
    }

    public IEnumerable<WorkspaceItemExtension> MatchWorkspaceItemExtensions(string file)
    {
        foreach (Extension extension in AllExtensions)
        {
            if (extension is WorkspaceItemExtension wsiExtension &&
                wsiExtension.IsSupported(file))
            {
                yield return wsiExtension;
            }
        }
    }
}

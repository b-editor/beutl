using BeUtl.Collections;
using BeUtl.Configuration;

using static BeUtl.Configuration.ExtensionConfig;

namespace BeUtl.Framework.Services;

public sealed class ExtensionProvider
{
    internal readonly Dictionary<int, Extension[]> _allExtensions = new();
    private readonly ExtensionConfig _config = GlobalConfiguration.Instance.ExtensionConfig;

    public ExtensionProvider(PackageManager packageManager)
    {
        PackageManager = packageManager;
    }

    public PackageManager PackageManager { get; }

    public IEnumerable<Extension> AllExtensions => _allExtensions.Values.SelectMany(ext => ext);

    public EditorExtension? MatchEditorExtension(string file)
    {
        string? fileExt = Path.GetExtension(file);

        if (_config.EditorExtensions.TryGetValue(fileExt, out ICoreList<TypeLazy>? list))
        {
            foreach (Extension extension in AllExtensions)
            {
                Type extType = extension.GetType();
                if (extension is not EditorExtension editorExtension) continue;

                foreach (TypeLazy type in list.AsSpan())
                {
                    if (extType == type.Type
                        && editorExtension.IsSupported(file))
                    {
                        return editorExtension;
                    }
                }
            }
        }

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

namespace Beutl.Editor.Services;

/// <summary>Reports files held by open editors before file-browser mutations.</summary>
public interface IEditorFileUsage
{
    bool IsFileInUse(string path, bool isDirectory);
}

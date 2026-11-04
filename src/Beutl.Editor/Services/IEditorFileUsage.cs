namespace Beutl.Editor.Services;

/// <summary>Reports files held by open editors before file-browser mutations.</summary>
public interface IEditorFileUsage
{
    bool IsFileInUse(string path, bool isDirectory);

    /// <summary>
    /// Reports whether any of the files is in use. An implementation can answer from one look at what the editors
    /// hold, where asking about each file looks again every time.
    /// </summary>
    bool IsAnyFileInUse(IReadOnlyCollection<(string Path, bool IsDirectory)> files)
    {
        return files.Any(file => IsFileInUse(file.Path, file.IsDirectory));
    }
}

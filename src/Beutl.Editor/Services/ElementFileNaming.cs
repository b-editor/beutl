namespace Beutl.Editor.Services;

public static class ElementFileNaming
{
    public static Uri GetUri(Uri sceneUri, Guid elementId)
    {
        ArgumentNullException.ThrowIfNull(sceneUri);
        if (!sceneUri.IsFile)
        {
            throw new ArgumentException("The scene URI must be an absolute file URI.", nameof(sceneUri));
        }

        string directory = Path.GetDirectoryName(sceneUri.LocalPath)
                           ?? throw new ArgumentException("The scene URI must have a directory.", nameof(sceneUri));
        string stem = elementId.ToString("N");
        string path = Path.Combine(directory, $"{stem}.{EditorConstants.ElementFileExtension}");

        for (int index = 1; File.Exists(path); index++)
        {
            path = Path.Combine(directory, $"{stem}-{index}.{EditorConstants.ElementFileExtension}");
        }

        return new Uri(path);
    }
}

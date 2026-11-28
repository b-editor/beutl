namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

internal sealed class LutLibraryFolderViewModel(string path)
{
    public string Path { get; } = path;

    public string Name { get; } = GetName(path);

    private static string GetName(string path)
    {
        string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 ? name : path;
    }
}

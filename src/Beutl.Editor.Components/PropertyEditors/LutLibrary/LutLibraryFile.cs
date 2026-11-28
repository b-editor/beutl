namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// One .cube file the library lists. Error is set when the file cannot be read as a LUT.
internal sealed record LutLibraryFile(string Path, string Name, bool IsFavorite, string? Error)
{
    public bool IsError => Error != null;
}

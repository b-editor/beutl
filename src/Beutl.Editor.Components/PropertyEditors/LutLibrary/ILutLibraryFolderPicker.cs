namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// A picker that can add folders of files, which gives the library picker its folder button.
internal interface ILutLibraryFolderPicker
{
    Task AddFolderAsync();
}

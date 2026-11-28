using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class CubeSourceEditorViewModel(IPropertyAdapter<CubeSource?> property)
    : LocalFileSourceEditorViewModel<CubeSource>(property)
{
    private LutLibraryStore? _libraryStore;

    // Tests and hosts can point the library at a folder other than <BeutlHome>/luts before it is first used.
    internal string? LibraryFolderPathOverride { get; set; }

    internal LutLibraryStore GetLibraryStore()
    {
        return _libraryStore ??= new LutLibraryStore(
            LibraryFolderPathOverride is { Length: > 0 } path
                ? path
                : Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "luts"));
    }

    public void SetValueAndCommit(CubeSource? oldValue, CubeSource? newValue)
    {
        if (!IsElementEditable) return;
        if (!EqualityComparer<CubeSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
                kf.Value.Value = newValue;
            }
            else
            {
                PropertyAdapter.SetValue(newValue);
            }

            Commit();
        }
    }
}

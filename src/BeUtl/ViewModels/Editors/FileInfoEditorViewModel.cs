using BeUtl.Services.Editors.Wrappers;

namespace BeUtl.ViewModels.Editors;

public sealed class FileInfoEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public FileInfoEditorViewModel(IWrappedProperty<FileInfo> property)
        : base(property)
    {
    }
}

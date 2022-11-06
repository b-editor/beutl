using Beutl.Framework;

namespace Beutl.ViewModels.Editors;

public sealed class FileInfoEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public FileInfoEditorViewModel(IAbstractProperty<FileInfo> property)
        : base(property)
    {
    }
}

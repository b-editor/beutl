using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class StorageFileEditorViewModel(IPropertyAdapter<FileInfo> property)
    : PropertyEditorViewModel<FileInfo, FileInfo?>(property)
{
    protected override FileInfo? ToEdit(FileInfo value) => value;

    protected override FileInfo FromEdit(FileInfo? edit) => edit!;

    public override void ConfirmEdit(FileInfo? oldValue, FileInfo? newValue)
    {
        if (IsDisposed) return;
        SetValue(ShownValue.Value, newValue);
    }
}

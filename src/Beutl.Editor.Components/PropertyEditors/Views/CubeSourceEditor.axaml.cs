using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CubeSourceEditor : UserControl
{
    public CubeSourceEditor()
    {
        InitializeComponent();

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("LUT File") { Patterns = ["*.cube"] }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not CubeSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        CubeSource? oldValue = vm.PropertyAdapter.GetValue();
        var newValue = new CubeSource();
        newValue.ReadFrom(new Uri(fi.FullName));
        vm.SetValueAndCommit(oldValue, newValue);
    }
}

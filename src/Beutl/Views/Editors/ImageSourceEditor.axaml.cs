using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Media.Source;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ImageSourceEditor : UserControl
{
    public ImageSourceEditor()
    {
        InitializeComponent();
        FileEditor.OpenOptions = SharedFilePickerOptions.OpenImage();
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not ImageSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        ImageSource? oldValue = vm.PropertyAdapter.GetValue();
        vm.SetValueAndDispose(oldValue, ImageSource.Open(fi.FullName));
    }
}

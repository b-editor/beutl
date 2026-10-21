using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.Views;

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

        vm.SetValue(ImageSource.Open(fi.FullName));
    }
}

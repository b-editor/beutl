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
        if (DataContext is not ImageSourceEditorViewModel vm) return;
        if (e.NewValue is not FileInfo fi) return;

        // 画像を開く
        if (!BitmapSource.TryOpen(fi.FullName, out BitmapSource? bitmapSource)) return;

        IImageSource? oldValue = vm.WrappedProperty.GetValue();
        vm.SetValueAndDispose(oldValue, bitmapSource);
    }
}

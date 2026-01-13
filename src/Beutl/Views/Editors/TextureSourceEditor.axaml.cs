using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics3D.Textures;
using Beutl.Media.Source;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class TextureSourceEditor : UserControl
{
    public TextureSourceEditor()
    {
        InitializeComponent();
        FileEditor.OpenOptions = SharedFilePickerOptions.OpenImage();
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not TextureSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        TextureSource? oldValue = vm.PropertyAdapter.GetValue();

        // Create new ImageTextureSource with the selected image file
        var imageSource = ImageSource.Open(fi.FullName);
        var textureSource = new ImageTextureSource();
        textureSource.Source.CurrentValue = imageSource;

        vm.SetValueAndDispose(oldValue, textureSource);
    }
}

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Beutl.Media.Source;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ImageSourceEditor : UserControl
{
    public ImageSourceEditor()
    {
        InitializeComponent();
        button.Click += Button_Click;
    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ImageSourceEditorViewModel vm || VisualRoot is not TopLevel topLevel) return;

        IReadOnlyList<IStorageFile> result = await topLevel.StorageProvider.OpenFilePickerAsync(SharedFilePickerOptions.OpenImage());
        if (result.Count > 0
            && result[0].TryGetLocalPath() is string localPath
            && BitmapSource.TryOpen(localPath, out BitmapSource? imageSource))
        {
            IImageSource? oldValue = vm.WrappedProperty.GetValue();
            vm.SetValueAndDispose(oldValue, imageSource);
        }
    }
}

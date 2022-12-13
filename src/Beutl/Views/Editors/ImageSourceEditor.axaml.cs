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

        var options = new FilePickerOpenOptions
        {
            FileTypeFilter = new FilePickerFileType[]
            {
                new FilePickerFileType("All Images")
                {
                    Patterns = new string[]
                    {
                        // SKEncodedImageFormat
                        "*.bmp",
                        "*.gif",
                        "*.ico",
                        "*.jpg",
                        "*.jpeg",
                        "*.png",
                        "*.wbmp",
                        "*.webp",
                        "*.pkm",
                        "*.ktx",
                        "*.astc",
                        "*.dng",
                        "*.heif",
                        "*.avif",
                    },
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                    MimeTypes = new[] { "image/*" }
                }
            }
        };

        IReadOnlyList<IStorageFile> result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (result.Count > 0
            && result[0].TryGetUri(out Uri? uri)
            && uri.IsFile
            && MediaSourceManager.Shared.OpenImageSource(uri.LocalPath, out IImageSource? imageSource))
        {
            IImageSource? oldValue = vm.WrappedProperty.GetValue();
            vm.SetValue(oldValue, imageSource);
            oldValue?.Dispose();
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using Beutl.Api.Objects;

using BeUtl.ViewModels.SettingsPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.SettingsPages.Dialogs;

public partial class SelectAvatarImage : ContentDialog, IStyleable
{
    public SelectAvatarImage()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    private async void UploadImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SelectAvatarImageViewModel viewModel && VisualRoot is Window parent)
        {
            var options = new FilePickerOpenOptions
            {
                FileTypeFilter = new FilePickerFileType[]
                {
                    FilePickerFileTypes.ImageAll
                }
            };
            IReadOnlyList<IStorageFile> result = await parent.StorageProvider.OpenFilePickerAsync(options);

            if (result.Count > 0
                && result[0].TryGetUri(out Uri? uri)
                && uri.IsFile)
            {
                Asset asset = await viewModel.UploadImage(uri.LocalPath, GetContentType(Path.GetExtension(uri.LocalPath)));
                ListBox.ScrollIntoView(asset);
            }
        }
    }

    private static string GetContentType(string extension)
    {
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => throw new InvalidOperationException(),
        };
    }
}

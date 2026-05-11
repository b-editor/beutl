using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class WindowCaptureDialog : ContentDialog
{
    public WindowCaptureDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WindowCaptureDialogViewModel vm) return;

        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var options = new FilePickerSaveOptions
        {
            Title = "Save Window Capture",
            DefaultExtension = "mp4",
            SuggestedFileName = $"beutl-capture-{DateTime.Now:yyyyMMdd-HHmmss}.mp4",
            SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Videos),
            FileTypeChoices =
            [
                new FilePickerFileType("MP4 video") { Patterns = ["*.mp4"] },
            ],
        };

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        if (file?.TryGetLocalPath() is { } localPath)
        {
            vm.OutputPath.Value = localPath;
        }
    }
}

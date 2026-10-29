using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.VideoUpload;

namespace Beutl.Views.Tools;

public partial class YouTubeUploadSettingsView : UserControl
{
    public YouTubeUploadSettingsView() => InitializeComponent();

    private async void SignInClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is YouTubeUploadContext context)
            await context.SignInAsync();
    }

    private async void ConfigureClientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not YouTubeUploadContext context || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.VideoUpload_SelectClient,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            using IStorageFile? file = files.FirstOrDefault();
            if (file?.TryGetLocalPath() is { } path)
                context.ConfigureClient(path);
        }
        catch (Exception)
        {
            context.ConnectionMessage.Value = Strings.VideoUpload_DesktopClientRequired;
        }
    }

    private void CancelSignInClick(object? sender, RoutedEventArgs e)
        => (DataContext as YouTubeUploadContext)?.CancelSignIn();

    private void SignOutClick(object? sender, RoutedEventArgs e)
        => (DataContext as YouTubeUploadContext)?.SignOut();

    private async void SetupHelpClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://developers.google.com/youtube/v3/guides/auth/installed-apps")
            { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { await ex.Handle(); }
    }
}

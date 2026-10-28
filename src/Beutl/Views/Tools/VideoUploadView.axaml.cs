using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Media.Decoding;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class VideoUploadView : UserControl
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<VideoUploadView, bool>(nameof(IsRunning));

    public VideoUploadView() => InitializeComponent();

    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? UploadRequested;

    private void UploadClick(object? sender, RoutedEventArgs e) => UploadRequested?.Invoke(this, e);

    private async void PreviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoUploadViewModel { CompletedFile.Value: { } path }) return;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException(Strings.VideoUpload_FileMissing);
            // This path can be restored from a shared project. Preview must not launch an
            // executable or script supplied as an alleged completed video.
            if (DecoderFileExtensions.Classify(path) is not (MediaFileKind.Video or MediaFileKind.Image))
                throw new InvalidOperationException(Strings.VideoUpload_PreviewUnavailable);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { await ex.Handle(); }
    }

    private async void OpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoUploadViewModel { CompletedFile.Value: { } path }) return;
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { await ex.Handle(); }
    }

    private async void OpenUploadedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoUploadViewModel { UploadedUrl.Value: { } url }
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { await ex.Handle(); }
    }
}

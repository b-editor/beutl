using FluentAvalonia.UI.Controls;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public partial class FFmpegInstallDialog : ContentDialog
{
    public FFmpegInstallDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnCloseButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnCloseButtonClick(args);
        if (DataContext is FFmpegInstallDialogViewModel vm)
        {
            vm.Cancel();
        }
    }

    protected override void OnClosed(ContentDialogClosedEventArgs args)
    {
        base.OnClosed(args);
        if (DataContext is FFmpegInstallDialogViewModel vm)
        {
            vm.Dispose();
        }
    }
}

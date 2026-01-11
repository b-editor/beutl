using FluentAvalonia.UI.Controls;

namespace Beutl.Extensions.FFmpeg;

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

using FluentAvalonia.UI.Controls;

namespace Beutl.Extensions.FFmpeg;

public partial class FFmpegInstallDialog : FAContentDialog
{
    public FFmpegInstallDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override void OnCloseButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnCloseButtonClick(args);
        if (DataContext is FFmpegInstallDialogViewModel vm)
        {
            vm.Cancel();
        }
    }

    protected override void OnClosed(FAContentDialogClosedEventArgs args)
    {
        base.OnClosed(args);
        if (DataContext is FFmpegInstallDialogViewModel vm)
        {
            vm.Dispose();
        }
    }
}

using Avalonia.Threading;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Services;

namespace Beutl.Extensions.FFmpeg;

internal static class FFmpegInstallNotifier
{
    private static int s_notified;

    public static void NotifyMissing()
    {
        if (Interlocked.Exchange(ref s_notified, 1) == 1)
            return;

        NotificationService.ShowError(
            Strings.FFmpegError,
            Strings.Make_sure_you_have_FFmpeg_installed,
            onClose: Reset,
            onActionButtonClick: ShowInstallDialog,
            actionButtonText: Strings.Install);
    }

    public static void Reset() => Interlocked.Exchange(ref s_notified, 0);

    public static void ShowInstallDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var viewModel = new FFmpegInstallDialogViewModel();
            var dialog = new FFmpegInstallDialog { DataContext = viewModel };
            dialog.ShowAsync();
            viewModel.Start();
        });
    }
}

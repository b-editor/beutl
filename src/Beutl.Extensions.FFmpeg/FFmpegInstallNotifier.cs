using Avalonia.Threading;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Services;

namespace Beutl.Extensions.FFmpeg;

internal static class FFmpegInstallNotifier
{
    private const long ThrottleMs = 10_000;
    private static long s_lastNotifiedTicks;

    public static void NotifyMissing()
    {
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref s_lastNotifiedTicks);
        if (last != 0 && now - last < ThrottleMs)
            return;
        Interlocked.Exchange(ref s_lastNotifiedTicks, now);

        NotificationService.ShowError(
            Strings.FFmpegError,
            Strings.Make_sure_you_have_FFmpeg_installed,
            onActionButtonClick: ShowInstallDialog,
            actionButtonText: Strings.Install);
    }

    private static void ShowInstallDialog()
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

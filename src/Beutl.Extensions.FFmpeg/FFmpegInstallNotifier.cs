using Avalonia.Threading;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Services;

namespace Beutl.Extensions.FFmpeg;

internal static class FFmpegInstallNotifier
{
    private const long ThrottleMs = 10_000;
    private static long s_lastNotifiedTicks;
    private static volatile bool s_librariesMissing;

    public static bool IsLibrariesMissing => s_librariesMissing;

    public static void NotifyMissing()
    {
        s_librariesMissing = true;

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

    public static void MarkInstalled()
    {
        s_librariesMissing = false;
        Interlocked.Exchange(ref s_lastNotifiedTicks, 0);
    }

    public static void MarkMissing()
    {
        s_librariesMissing = true;
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

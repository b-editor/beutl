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

    internal static event EventHandler? AvailabilityChanged;

    public static void NotifyMissing()
    {
        SetLibrariesMissing(true);
        if (!TryAcquireNotifySlot(Environment.TickCount64))
            return;

        NotificationService.ShowError(
            Strings.FFmpegError,
            Strings.Make_sure_you_have_FFmpeg_installed,
            onActionButtonClick: ShowInstallDialog,
            actionButtonText: Strings.Install);
    }

    // CAS guard: only the thread whose CompareExchange swaps `last` -> `now`
    // wins the right to fire the notification. Concurrent losers bail so a
    // single failure observed on multiple background threads (extension load
    // / encoder setup / worker process startup) yields one toast, not many.
    internal static bool TryAcquireNotifySlot(long now)
    {
        long last = Interlocked.Read(ref s_lastNotifiedTicks);
        if (last != 0 && now - last < ThrottleMs)
            return false;
        return Interlocked.CompareExchange(ref s_lastNotifiedTicks, now, last) == last;
    }

    public static void MarkInstalled()
    {
        SetLibrariesMissing(false, notifyWhenUnchanged: true);
        Interlocked.Exchange(ref s_lastNotifiedTicks, 0);
    }

    public static void MarkMissing()
    {
        SetLibrariesMissing(true);
    }

    internal static void MarkVerificationStarted()
    {
        SetLibrariesMissing(false, notify: false);
        Interlocked.Exchange(ref s_lastNotifiedTicks, 0);
    }

    private static void SetLibrariesMissing(bool value, bool notify = true, bool notifyWhenUnchanged = false)
    {
        bool changed = s_librariesMissing != value;
        if (!changed && !notifyWhenUnchanged)
            return;

        s_librariesMissing = value;
        if (notify)
            AvailabilityChanged?.Invoke(null, EventArgs.Empty);
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

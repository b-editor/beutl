using Avalonia.Threading;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Services;

namespace Beutl.Extensions.FFmpeg;

internal static class FFmpegInstallNotifier
{
    private const long ThrottleMs = 10_000;

    // After libraries are reported missing, skip fresh worker-start probes for this long, then allow
    // a probe so a transient failure (momentary file lock, crashed worker) can self-recover without
    // the user re-running the install wizard. A genuinely-missing install just re-arms the cooldown.
    private const long ReprobeCooldownMs = 5_000;
    private static long s_lastNotifiedTicks;
    private static long s_missingSinceTicks;
    private static volatile bool s_librariesMissing;

    public static bool IsLibrariesMissing => s_librariesMissing;

    internal static long MissingSinceTicks => Interlocked.Read(ref s_missingSinceTicks);

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
        ArmReprobeCooldown();
    }

    // A worker process handshaked successfully, so FFmpeg loaded: clear any missing latch. This is
    // the self-recovery path for a transient failure that had latched the queue.
    internal static void NotifyWorkerStarted()
    {
        SetLibrariesMissing(false);
    }

    // Start the re-probe throttle window. Called only when a real worker-start attempt observed the
    // libraries missing, so gate short-circuits (which never start a worker) cannot keep pushing the
    // window forward and re-latch the queue.
    internal static void ArmReprobeCooldown()
    {
        Interlocked.Exchange(ref s_missingSinceTicks, Environment.TickCount64);
    }

    // True while a fresh worker-start probe should be skipped (libraries reported missing and the
    // re-probe cooldown has not elapsed). After the cooldown, callers should attempt a real start so
    // the outcome re-probes actual FFmpeg availability instead of trusting the sticky flag.
    internal static bool ShouldSkipStartProbe(long now)
    {
        if (!s_librariesMissing)
            return false;

        long since = Interlocked.Read(ref s_missingSinceTicks);
        return since != 0 && now - since < ReprobeCooldownMs;
    }

    internal static void MarkVerificationStarted()
    {
        SetLibrariesMissing(false, notify: false);
        Interlocked.Exchange(ref s_lastNotifiedTicks, 0);
    }

    private static void SetLibrariesMissing(bool value, bool notify = true, bool notifyWhenUnchanged = false)
    {
        bool changed = s_librariesMissing != value;
        if (!value)
            Interlocked.Exchange(ref s_missingSinceTicks, 0);

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

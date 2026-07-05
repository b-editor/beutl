namespace Beutl.Extensions.FFmpeg;

public static class FFmpegLibraryState
{
    // After libraries are reported missing, skip fresh worker-start probes for this long, then allow
    // a probe so a transient failure (momentary file lock, crashed worker) can self-recover without
    // the user re-running the install wizard. A genuinely-missing install just re-arms the cooldown.
    private const long ReprobeCooldownMs = 5_000;
    private static volatile bool s_librariesMissing;
    private static long s_missingSinceTicks;

    public static event EventHandler? LibrariesMissing;

    // Fires on every availability transition (missing <-> available) so consumers such as proxy
    // generation can pause and resume. MarkInstalled can force a signal even on an unchanged state.
    public static event EventHandler? AvailabilityChanged;

    public static bool IsLibrariesMissing => s_librariesMissing;

    public static long MissingSinceTicks => Interlocked.Read(ref s_missingSinceTicks);

    public static void NotifyMissing()
    {
        SetLibrariesMissing(true);
        LibrariesMissing?.Invoke(null, EventArgs.Empty);
    }

    public static void MarkInstalled() => SetLibrariesMissing(false, notifyWhenUnchanged: true);

    public static void MarkMissing()
    {
        // Arm the cooldown before notifying: SetLibrariesMissing raises AvailabilityChanged, and a
        // listener that reacts synchronously must already see ShouldSkipStartProbe == true, otherwise
        // it can immediately re-probe the worker before the cooldown is in effect.
        ArmReprobeCooldown();
        SetLibrariesMissing(true);
    }

    // A worker process handshaked successfully, so FFmpeg loaded: clear any missing latch. This is
    // the self-recovery path for a transient failure that had latched the queue.
    public static void NotifyWorkerStarted() => SetLibrariesMissing(false);

    // Clear the missing latch without signaling availability, used while a verification/install run
    // is in progress so consumers do not prematurely resume before the outcome is known.
    public static void MarkVerificationStarted() => SetLibrariesMissing(false, notify: false);

    // Start the re-probe throttle window. Called only when a real worker-start attempt observed the
    // libraries missing, so gate short-circuits (which never start a worker) cannot keep pushing the
    // window forward and re-latch the queue.
    public static void ArmReprobeCooldown()
        => Interlocked.Exchange(ref s_missingSinceTicks, Environment.TickCount64);

    // True while a fresh worker-start probe should be skipped (libraries reported missing and the
    // re-probe cooldown has not elapsed). After the cooldown, callers should attempt a real start so
    // the outcome re-probes actual FFmpeg availability instead of trusting the sticky flag.
    public static bool ShouldSkipStartProbe(long now)
    {
        if (!s_librariesMissing)
            return false;

        long since = Interlocked.Read(ref s_missingSinceTicks);
        return since != 0 && now - since < ReprobeCooldownMs;
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
}

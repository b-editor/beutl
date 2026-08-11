namespace Beutl.Extensions.FFmpeg;

public static class FFmpegLibraryState
{
    // After libraries are reported missing, skip fresh worker-start probes for this long, then allow
    // a probe so a transient failure (momentary file lock, crashed worker) can self-recover without
    // the user re-running the install wizard. A genuinely-missing install just re-arms the cooldown.
    // 30s bounds a missing-FFmpeg session to ~2 worker-start attempts per minute, so the worker's own
    // "libraries not found" stdout error does not repeat on every frame/thumbnail request.
    private const long ReprobeCooldownMs = 30_000;
    private static readonly object s_stateGate = new();
    private static readonly object s_notificationGate = new();
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
        lock (s_notificationGate)
        {
            bool shouldNotify;
            lock (s_stateGate)
                shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);

            if (shouldNotify)
                AvailabilityChanged?.Invoke(null, EventArgs.Empty);

            LibrariesMissing?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void MarkInstalled() => SetLibrariesMissing(false, notifyWhenUnchanged: true);

    public static void MarkMissing()
    {
        lock (s_notificationGate)
        {
            bool shouldNotify;
            lock (s_stateGate)
            {
                // Arm the cooldown before notifying: SetLibrariesMissing raises AvailabilityChanged,
                // and a listener that reacts synchronously must already see ShouldSkipStartProbe ==
                // true, otherwise it can immediately re-probe the worker before the cooldown is in
                // effect.
                ArmReprobeCooldownCore();
                shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);
            }

            if (shouldNotify)
                AvailabilityChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    // Record the missing latch observed by a decode attempt WITHOUT re-arming the cooldown (a real
    // worker-start failure arms it; re-arming on a short-circuited decode would keep the re-probe
    // window from ever elapsing). Returns whether the condition was already known, so callers can
    // log a first discovery as an error and an already-known short-circuit quietly.
    public static bool RecordMissingObserved()
    {
        lock (s_notificationGate)
        {
            bool wasKnownMissing;
            bool shouldNotify;
            lock (s_stateGate)
            {
                wasKnownMissing = s_librariesMissing;
                shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);
            }

            if (shouldNotify)
                AvailabilityChanged?.Invoke(null, EventArgs.Empty);

            return wasKnownMissing;
        }
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
    {
        lock (s_notificationGate)
        {
            lock (s_stateGate)
                ArmReprobeCooldownCore();
        }
    }

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
        lock (s_notificationGate)
        {
            bool shouldNotify;
            lock (s_stateGate)
                shouldNotify = SetLibrariesMissingCore(value, notify, notifyWhenUnchanged);

            if (!notify)
                return;

            if (shouldNotify)
                AvailabilityChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool SetLibrariesMissingCore(bool value, bool notify, bool notifyWhenUnchanged)
    {
        bool changed = s_librariesMissing != value;
        if (!value)
            Interlocked.Exchange(ref s_missingSinceTicks, 0);

        if (!changed && !notifyWhenUnchanged)
            return false;

        s_librariesMissing = value;
        return notify;
    }

    private static void ArmReprobeCooldownCore()
        => Interlocked.Exchange(ref s_missingSinceTicks, Environment.TickCount64);
}

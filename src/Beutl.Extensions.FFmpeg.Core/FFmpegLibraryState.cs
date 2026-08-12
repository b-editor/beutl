namespace Beutl.Extensions.FFmpeg;

public sealed class FFmpegLibraryAvailabilityChangedEventArgs(bool isLibrariesMissing) : EventArgs
{
    public bool IsLibrariesMissing { get; } = isLibrariesMissing;
}

public static class FFmpegLibraryState
{
    private enum NotificationKind
    {
        AvailabilityChanged,
        LibrariesMissing,
    }

    // After libraries are reported missing, skip fresh worker-start probes for this long, then allow
    // a probe so a transient failure (momentary file lock, crashed worker) can self-recover without
    // the user re-running the install wizard. A genuinely-missing install just re-arms the cooldown.
    // 30s bounds a missing-FFmpeg session to ~2 worker-start attempts per minute, so the worker's own
    // "libraries not found" stdout error does not repeat on every frame/thumbnail request.
    private const long ReprobeCooldownMs = 30_000;
    private static readonly object s_stateGate = new();
    // This gate protects only the pending notification queue. It is never held while invoking
    // subscribers, so callbacks can synchronously dispatch another state transition on the
    // dispatcher thread without a cross-thread lock cycle.
    private static readonly object s_notificationGate = new();
    private static readonly LinkedList<PendingNotification> s_pendingNotifications = new();
    // This marker flows into callback-created tasks. Those descendants enqueue their transition and
    // return instead of waiting for the dispatcher that is still executing their originating callback.
    // They never claim queue entries; FIFO delivery remains exclusively owned by DrainAsDispatcher.
    private static readonly AsyncLocal<NotificationDispatchContext?> s_dispatchContext = new();
    private static bool s_isDispatchingNotifications;
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
        PendingNotification? availabilityNotification = null;
        PendingNotification missingNotification;
        lock (s_stateGate)
        {
            bool shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);

            if (shouldNotify)
                availabilityNotification = QueueNotificationCore(
                    NotificationKind.AvailabilityChanged,
                    isLibrariesMissing: true);
            missingNotification = QueueNotificationCore(NotificationKind.LibrariesMissing, isLibrariesMissing: true);
        }

        DrainNotifications(availabilityNotification, missingNotification);
    }

    public static void MarkInstalled() => SetLibrariesMissing(false, notifyWhenUnchanged: true);

    public static void MarkMissing()
    {
        PendingNotification? notification = null;
        lock (s_stateGate)
        {
            bool shouldNotify;
            // Arm the cooldown before notifying: SetLibrariesMissing raises AvailabilityChanged, and
            // a listener that reacts synchronously must already see ShouldSkipStartProbe == true,
            // otherwise it can immediately re-probe the worker before the cooldown is in effect.
            ArmReprobeCooldownCore();
            shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);

            if (shouldNotify)
                notification = QueueNotificationCore(NotificationKind.AvailabilityChanged, isLibrariesMissing: true);
        }

        DrainNotifications(notification);
    }

    // Mark libraries missing after a worker failure, but preserve an active cooldown when this is
    // only a repeated short-circuit observation from another caller.
    public static void MarkMissingIfNeeded()
    {
        PendingNotification? notification = null;
        lock (s_stateGate)
        {
            if (!ShouldSkipStartProbeCore(Environment.TickCount64))
                ArmReprobeCooldownCore();

            bool shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);
            if (shouldNotify)
                notification = QueueNotificationCore(NotificationKind.AvailabilityChanged, isLibrariesMissing: true);
        }

        DrainNotifications(notification);
    }

    // Record the missing latch observed by a decode attempt WITHOUT re-arming the cooldown (a real
    // worker-start failure arms it; re-arming on a short-circuited decode would keep the re-probe
    // window from ever elapsing). Returns whether the condition was already known, so callers can
    // log a first discovery as an error and an already-known short-circuit quietly.
    public static bool RecordMissingObserved()
    {
        bool wasKnownMissing = RecordMissingObservedCore(out PendingNotification? notification);
        DrainNotifications(notification);
        return wasKnownMissing;
    }

    internal static bool RecordMissingObservedDeferred(out Action dispatchNotifications)
    {
        bool wasKnownMissing = RecordMissingObservedCore(out PendingNotification? notification);
        dispatchNotifications = () => DrainNotifications(notification);
        return wasKnownMissing;
    }

    private static bool RecordMissingObservedCore(out PendingNotification? notification)
    {
        bool wasKnownMissing;
        notification = null;
        lock (s_stateGate)
        {
            bool shouldNotify;
            wasKnownMissing = s_librariesMissing;
            shouldNotify = SetLibrariesMissingCore(true, notify: true, notifyWhenUnchanged: false);

            if (shouldNotify)
                notification = QueueNotificationCore(NotificationKind.AvailabilityChanged, isLibrariesMissing: true);
        }

        return wasKnownMissing;
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
        lock (s_stateGate)
            ArmReprobeCooldownCore();
    }

    // True while a fresh worker-start probe should be skipped (libraries reported missing and the
    // re-probe cooldown has not elapsed). After the cooldown, callers should attempt a real start so
    // the outcome re-probes actual FFmpeg availability instead of trusting the sticky flag.
    public static bool ShouldSkipStartProbe(long now)
    {
        return ShouldSkipStartProbeCore(now);
    }

    private static void SetLibrariesMissing(bool value, bool notify = true, bool notifyWhenUnchanged = false)
    {
        PendingNotification? notification = null;
        lock (s_stateGate)
        {
            bool shouldNotify = SetLibrariesMissingCore(value, notify, notifyWhenUnchanged);
            if (notify && shouldNotify)
                notification = QueueNotificationCore(NotificationKind.AvailabilityChanged, value);
        }

        if (notify)
            DrainNotifications(notification);
    }

    private static PendingNotification QueueNotificationCore(NotificationKind kind, bool isLibrariesMissing)
    {
        var notification = new PendingNotification(kind, isLibrariesMissing, hasOwner: s_dispatchContext.Value is null);
        lock (s_notificationGate)
            s_pendingNotifications.AddLast(notification);
        return notification;
    }

    private static void DrainNotifications(params PendingNotification?[] notifications)
    {
        bool becameDispatcher;
        lock (s_notificationGate)
        {
            becameDispatcher = !s_isDispatchingNotifications;
            if (becameDispatcher)
            {
                s_isDispatchingNotifications = true;
            }
        }

        if (becameDispatcher)
        {
            DrainAsDispatcher();
            AwaitOwnedNotifications(notifications);
            return;
        }

        if (s_dispatchContext.Value is not null)
            return;

        foreach (PendingNotification? notification in notifications)
        {
            if (notification is null)
                continue;

            notification.Completion.Task.GetAwaiter().GetResult();
        }
    }

    private static void DrainAsDispatcher()
    {
        while (true)
        {
            PendingNotification notification;
            lock (s_notificationGate)
            {
                if (s_pendingNotifications.Count == 0)
                {
                    s_isDispatchingNotifications = false;
                    break;
                }

                notification = s_pendingNotifications.First!.Value;
                s_pendingNotifications.RemoveFirst();
            }

            InvokeNotification(notification);
        }
    }

    private static void AwaitOwnedNotifications(PendingNotification?[] notifications)
    {
        Exception? firstException = null;
        foreach (PendingNotification? notification in notifications)
        {
            if (notification is null)
                continue;

            try
            {
                notification.Completion.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                firstException ??= ex;
            }
        }

        if (firstException is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstException).Throw();
    }

    private static void InvokeNotification(PendingNotification notification)
    {
        NotificationDispatchContext? previousContext = s_dispatchContext.Value;
        s_dispatchContext.Value = new NotificationDispatchContext();
        try
        {
            switch (notification.Kind)
            {
                case NotificationKind.AvailabilityChanged:
                    AvailabilityChanged?.Invoke(
                        null,
                        new FFmpegLibraryAvailabilityChangedEventArgs(notification.IsLibrariesMissing));
                    break;
                case NotificationKind.LibrariesMissing:
                    LibrariesMissing?.Invoke(null, EventArgs.Empty);
                    break;
            }

            notification.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            notification.Completion.TrySetException(ex);
            if (!notification.HasOwner)
                _ = notification.Completion.Task.Exception;
        }
        finally
        {
            s_dispatchContext.Value = previousContext;
        }
    }

    private sealed class NotificationDispatchContext
    {
    }

    private sealed class PendingNotification(NotificationKind kind, bool isLibrariesMissing, bool hasOwner)
    {
        public NotificationKind Kind { get; } = kind;

        public bool IsLibrariesMissing { get; } = isLibrariesMissing;

        public bool HasOwner { get; } = hasOwner;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private static bool ShouldSkipStartProbeCore(long now)
    {
        if (!s_librariesMissing)
            return false;

        long since = Interlocked.Read(ref s_missingSinceTicks);
        return since != 0 && now - since < ReprobeCooldownMs;
    }
}

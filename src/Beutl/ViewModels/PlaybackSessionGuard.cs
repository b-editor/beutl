namespace Beutl.ViewModels;

// Arbitrates ownership of PlayerViewModel's single shared playback session. Play() and StartShuttle
// claim a new session; a Pause() timeout that abandons a stuck playback task disowns it. A playback
// task captures its token at start and only restores shared session state (IsPlaying, the preview
// subscriptions, the loop re-arm) while it still owns the session, so a task that a timeout abandoned
// cannot stomp the session that replaced it when it finally unblocks.
internal sealed class PlaybackSessionGuard
{
    private readonly object _sync = new();
    private int _generation;

    // Claim a fresh session and return the token that identifies the claiming task.
    public int Claim() => Claim(static () => { });

    // Claim and initialize the shared session state atomically with respect to a prior owner's
    // final guarded write. This prevents a late failure from clearing the new owner's state.
    public int Claim(Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        lock (_sync)
        {
            int token = Interlocked.Increment(ref _generation);
            initialize();
            return token;
        }
    }

    // True only while <paramref name="token"/> is still the current session (no later Claim/Disown).
    public bool Owns(int token) => Volatile.Read(ref _generation) == token;

    // Disown the current owner (a Pause() timeout abandoning a stuck task) so its late restore
    // becomes a no-op; the next Claim() starts a new session.
    public void Disown() => Disown(static () => { });

    public void Disown(Action restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        lock (_sync)
        {
            Interlocked.Increment(ref _generation);
            restore();
        }
    }

    public bool TryApply(int token, Action update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_sync)
        {
            if (_generation != token)
                return false;

            update();
            return true;
        }
    }
}

namespace Beutl.ViewModels;

// Ownership of PlayerViewModel's playback session: Play() and StartShuttle claim it and Pause() gives it up,
// so a task a timeout abandoned cannot overwrite the state of the session that replaced it.
internal sealed class PlaybackSessionGuard
{
    private readonly object _sync = new();
    private int _generation;

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

    // Disown the current owner on pause so its late restore
    // becomes a no-op; the next Claim starts a new session.
    public void Disown()
    {
        lock (_sync)
        {
            Interlocked.Increment(ref _generation);
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

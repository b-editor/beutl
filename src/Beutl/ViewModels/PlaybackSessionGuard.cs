namespace Beutl.ViewModels;

// Arbitrates ownership of PlayerViewModel's single shared playback session. Play() and StartShuttle
// claim a new session; a Pause() timeout that abandons a stuck playback task disowns it. A playback
// task captures its token at start and only restores shared session state (IsPlaying, the preview
// subscriptions, the loop re-arm) while it still owns the session, so a task that a timeout abandoned
// cannot stomp the session that replaced it when it finally unblocks.
internal sealed class PlaybackSessionGuard
{
    private int _generation;

    // Claim a fresh session and return the token that identifies the claiming task.
    public int Claim() => Interlocked.Increment(ref _generation);

    // True only while <paramref name="token"/> is still the current session (no later Claim/Disown).
    public bool Owns(int token) => Volatile.Read(ref _generation) == token;

    // Disown the current owner (a Pause() timeout abandoning a stuck task) so its late restore
    // becomes a no-op; the next Claim() starts a new session.
    public void Disown() => Interlocked.Increment(ref _generation);
}

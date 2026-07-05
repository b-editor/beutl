using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlaybackSessionGuardTests
{
    [Test]
    public void The_latest_claim_owns_the_session_and_older_claims_do_not()
    {
        var guard = new PlaybackSessionGuard();

        int first = guard.Claim();
        Assert.That(guard.Owns(first), Is.True, "the only claim owns the session");

        int second = guard.Claim();
        Assert.Multiple(() =>
        {
            Assert.That(guard.Owns(second), Is.True, "the newest claim owns the session");
            Assert.That(guard.Owns(first), Is.False, "a superseded claim no longer owns the session");
            Assert.That(second, Is.Not.EqualTo(first), "each claim gets a distinct token");
        });
    }

    [Test]
    public void Disown_makes_the_current_owner_stop_owning()
    {
        var guard = new PlaybackSessionGuard();
        int token = guard.Claim();

        guard.Disown();

        Assert.That(guard.Owns(token), Is.False,
            "a Pause() timeout disowns the running task so its late restore becomes a no-op");
    }

    // Models the exact race the generation guard fixes: a playback task (session A) is abandoned by a
    // Pause() timeout, a new Play() starts session B, and A only unblocks afterward. A must not still
    // own the session, or its finally would stomp B's IsPlaying / preview subscriptions.
    [Test]
    public void An_abandoned_session_does_not_own_after_a_timeout_disown_and_a_restart()
    {
        var guard = new PlaybackSessionGuard();
        int sessionA = guard.Claim();

        guard.Disown();                 // Pause() timed out and abandoned session A
        int sessionB = guard.Claim();   // a new Play() took over

        Assert.Multiple(() =>
        {
            Assert.That(guard.Owns(sessionA), Is.False, "the abandoned task must not restore/stomp the new session");
            Assert.That(guard.Owns(sessionB), Is.True, "the new session owns the shared playback state");
        });
    }
}

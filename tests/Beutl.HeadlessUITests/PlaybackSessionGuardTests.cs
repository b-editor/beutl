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

    [Test]
    public void TryApply_only_updates_state_for_the_current_session()
    {
        var guard = new PlaybackSessionGuard();
        int first = guard.Claim();
        int second = guard.Claim();
        int state = 0;

        bool staleApplied = guard.TryApply(first, () => state = 1);
        bool currentApplied = guard.TryApply(second, () => state = 2);

        Assert.Multiple(() =>
        {
            Assert.That(staleApplied, Is.False, "a superseded timer cannot mutate shared playback state");
            Assert.That(currentApplied, Is.True, "the current playback session can update shared state");
            Assert.That(state, Is.EqualTo(2));
        });
    }

    [Test]
    public void Claim_and_Disown_apply_their_state_changes_with_the_generation_change()
    {
        var guard = new PlaybackSessionGuard();
        int state = 0;

        int token = guard.Claim(() => state = 1);
        guard.Disown(() => state = 2);

        Assert.Multiple(() =>
        {
            Assert.That(guard.Owns(token), Is.False);
            Assert.That(state, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task A_new_claim_waits_for_the_current_guarded_update_before_initializing()
    {
        var guard = new PlaybackSessionGuard();
        int first = guard.Claim();
        int state = 0;
        using var updateEntered = new ManualResetEventSlim();
        using var releaseUpdate = new ManualResetEventSlim();
        using var claimStarted = new ManualResetEventSlim();

        Task<bool> updateTask = Task.Run(() => guard.TryApply(first, () =>
        {
            updateEntered.Set();
            if (!releaseUpdate.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The guarded update was not released within the test timeout.");
            }

            state = 1;
        }));
        Assert.That(updateEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        Task<int> claimTask = Task.Run(() =>
        {
            claimStarted.Set();
            return guard.Claim(() => state = 2);
        });

        try
        {
            Assert.That(claimStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            await Task.Delay(20);
            Assert.That(claimTask.IsCompleted, Is.False,
                "a new session cannot initialize while the old session is applying a final update");
        }
        finally
        {
            releaseUpdate.Set();
        }

        Assert.That(await updateTask, Is.True);
        int second = await claimTask;
        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(2), "the newer session initialization must be the last write");
            Assert.That(guard.Owns(second), Is.True);
        });
    }
}

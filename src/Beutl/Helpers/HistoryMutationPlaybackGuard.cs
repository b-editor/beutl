using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal sealed class HistoryMutationPlaybackGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async ValueTask<bool> RunAsync(
        IPreviewPlayer? player,
        Action drainPendingMutations,
        Func<bool> shouldPause,
        Func<bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(drainPendingMutations);
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync();
        try
        {
            // Flush debounced work (e.g. a pending nudge) before the pause decision: mutate()
            // fires the same flush hook, so a still-pending edit would be invisible to
            // shouldPause() yet get committed-then-reverted while playback is still live.
            // Draining first makes shouldPause() reflect the real post-flush state.
            drainPendingMutations();

            if (shouldPause() && player is not null)
            {
                // Pause() is a no-op when nothing is playing, but still awaits any
                // in-flight drain, so call it whenever a mutation is pending rather
                // than gating on IsPlaying (which a mid-drain pause has already cleared).
                await player.Pause();

                // A Play() that slipped in while that Pause was draining only checks
                // IsPlaying (already cleared), so it can restart the preview before we
                // mutate. Re-pause until the player stays stopped; the final check and
                // mutate() run on one synchronous turn (callers invoke the guard on the
                // UI thread), so no restart can interleave between them.
                while (player.IsPlaying.Value)
                {
                    await player.Pause();
                }
            }

            return mutate();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        // Disposing a SemaphoreSlim with a pending WaitAsync is undefined, so skip it
        // while the gate is held and let the GC reclaim it (no unmanaged handle is held).
        if (_gate.Wait(0))
        {
            _gate.Dispose();
        }
    }
}

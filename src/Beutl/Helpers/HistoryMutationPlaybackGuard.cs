using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal sealed class HistoryMutationPlaybackGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    internal async ValueTask<bool> RunAsync(
        IPreviewPlayer? player,
        Action drainPendingMutations,
        Func<bool> shouldPause,
        Func<bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(drainPendingMutations);
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(mutate);
        // Reject post-disposal callers. Relying on the disposed _gate to throw is unreliable:
        // Dispose() leaves the gate intact when an operation still holds it (see Dispose).
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync();
        try
        {
            // Re-check after acquiring the gate: a caller that was already queued in WaitAsync()
            // when Dispose() ran passed the pre-wait check, so reject it here too.
            ObjectDisposedException.ThrowIf(_disposed, this);

            // shouldPause() reflects whether this operation will change scene state, including
            // any pending transaction the drain below will commit (callers' predicates check
            // HasPendingOperations). Evaluate it before draining so the pause can bracket the
            // drain: a flush that commits pending work (e.g. a nudge) schedules frame-cache
            // rebuilds, which must not race a live player.
            if (shouldPause() && player is not null)
            {
                // Pause() is a no-op when nothing is playing, but still awaits any
                // in-flight drain, so call it whenever a mutation is pending rather
                // than gating on IsPlaying (which a mid-drain pause has already cleared).
                await player.Pause();

                // A Play() that slipped in while that Pause was draining only checks
                // IsPlaying (already cleared), so it can restart the preview before we
                // mutate. Re-pause until the player stays stopped.
                while (player.IsPlaying.Value)
                {
                    await player.Pause();
                }
            }

            // Drain debounced work (e.g. a pending nudge) and mutate only after the player is
            // confirmed stopped. Both run on one synchronous turn after the last Pause await
            // (callers invoke the guard on the UI thread), so no restart can interleave here.
            drainPendingMutations();
            return mutate();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Dispose the gate only when no operation holds it; if one is in flight, let the GC
        // reclaim the SemaphoreSlim on completion (no unmanaged handle is held).
        _disposed = true;
        if (_gate.Wait(0))
        {
            _gate.Dispose();
        }
    }
}

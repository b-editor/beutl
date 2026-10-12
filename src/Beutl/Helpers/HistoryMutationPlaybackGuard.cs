using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal sealed class HistoryMutationPlaybackGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    internal async ValueTask<TResult> RunAsync<TResult>(
        IPreviewPlayer? player,
        Action drainPendingMutations,
        Func<bool> shouldPause,
        Func<TResult> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drainPendingMutations);
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(mutate);
        // Reject post-disposal callers. Relying on the disposed _gate to throw is unreliable:
        // Dispose() leaves the gate intact when an operation still holds it (see Dispose).
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the gate: a caller that was already queued in WaitAsync()
            // when Dispose() ran passed the pre-wait check, so reject it here too.
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The drain commits pending work and rebuilds the frame cache, which must not race a live player,
            // so evaluate shouldPause() before the drain.
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

            // Drain and mutate in one synchronous turn after the last Pause, so no restart can interleave.
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

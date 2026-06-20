using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal sealed class HistoryMutationPlaybackGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async ValueTask<bool> RunAsync(IPreviewPlayer? player, Func<bool> shouldPause, Func<bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync();
        try
        {
            if (shouldPause() && player is not null)
            {
                // Pause() is a no-op when nothing is playing, but still awaits any
                // in-flight drain, so call it whenever a mutation is pending rather
                // than gating on IsPlaying (which a mid-drain pause has already cleared).
                await player.Pause();
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

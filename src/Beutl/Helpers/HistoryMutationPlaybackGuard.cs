using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal sealed class HistoryMutationPlaybackGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _pauseTask;

    internal async ValueTask<bool> RunAsync(IPreviewPlayer? player, Func<bool> shouldPause, Func<bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync();
        try
        {
            if (shouldPause())
            {
                await PauseIfNeededAsync(player);
            }

            return mutate();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask PauseIfNeededAsync(IPreviewPlayer? player)
    {
        if (player?.IsPlaying.Value == true)
        {
            _pauseTask = player.Pause();
        }

        Task? pauseTask = _pauseTask;
        if (pauseTask is null)
        {
            return;
        }

        try
        {
            await pauseTask;
        }
        finally
        {
            if (ReferenceEquals(_pauseTask, pauseTask) && pauseTask.IsCompleted)
            {
                _pauseTask = null;
            }
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}

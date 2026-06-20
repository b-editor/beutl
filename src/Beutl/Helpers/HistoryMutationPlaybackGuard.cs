using Beutl.Editor.Services;

namespace Beutl.Helpers;

internal static class HistoryMutationPlaybackGuard
{
    internal static async ValueTask PauseIfPlayingAsync(IPreviewPlayer? player)
    {
        if (player?.IsPlaying.Value == true)
        {
            await player.Pause();
        }
    }
}

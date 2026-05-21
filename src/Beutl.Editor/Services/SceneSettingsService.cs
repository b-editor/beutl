using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class SceneSettingsService : ISceneSettingsService
{
    private readonly HistoryManager _historyManager;

    public SceneSettingsService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool Apply(Scene scene, PixelSize frameSize, TimeSpan start, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.FrameSize == frameSize
            && scene.Start == start
            && scene.Duration == duration)
        {
            return false;
        }

        // Order matters less than atomicity — all three writes batch into the
        // current transaction and collapse into one history entry on Commit.
        scene.FrameSize = frameSize;
        scene.Start = start;
        scene.Duration = duration;

        _historyManager.Commit(CommandNames.ChangeSceneSettings);
        return true;
    }
}

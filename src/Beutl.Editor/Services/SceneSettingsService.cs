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

    public bool Apply(Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.FrameSize.Value == frameSize
            && scene.Start.Value == start
            && scene.Duration.Value == duration)
        {
            return false;
        }

        // These writes batch into one transaction, collapsing to a single history entry on Commit.
        scene.FrameSize.Value = frameSize;
        scene.Start.Value = start;
        scene.Duration.Value = duration;

        _historyManager.Commit(CommandNames.ChangeSceneSettings);
        return true;
    }
}

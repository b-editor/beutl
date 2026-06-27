using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class SceneSettingsService : ISceneSettingsService
{
    private readonly HistoryManager _historyManager;

    public SceneSettingsService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool Apply(Scene scene, PixelSize frameSize, TimeSpan start, TimeSpan duration, PreviewSourceMode previewSourceMode)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.FrameSize == frameSize
            && scene.Start == start
            && scene.Duration == duration
            && scene.PreviewSourceMode == previewSourceMode)
        {
            return false;
        }

        // These writes batch into one transaction, collapsing to a single history entry on Commit.
        scene.FrameSize = frameSize;
        scene.Start = start;
        scene.Duration = duration;
        scene.PreviewSourceMode = previewSourceMode;

        _historyManager.Commit(CommandNames.ChangeSceneSettings);
        return true;
    }
}

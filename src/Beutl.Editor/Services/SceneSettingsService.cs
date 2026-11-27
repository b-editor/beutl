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
        => Apply(scene, frameSize, start, duration, scene.GetSpeakerLayout());

    public bool Apply(Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration, SpeakerLayout channelLayout)
        => Apply(scene, frameSize, start, duration, channelLayout, scene.MasterVolume.Value);

    public bool Apply(
        Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration, SpeakerLayout channelLayout, float masterVolume)
        => Apply(scene, frameSize, start, duration, scene.FrameRate.Value, scene.SampleRate.Value, channelLayout, masterVolume);

    public bool Apply(
        Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration,
        Rational frameRate, int sampleRate, SpeakerLayout channelLayout, float masterVolume)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.FrameSize.Value == frameSize
            && scene.Start.Value == start
            && scene.Duration.Value == duration
            && scene.FrameRate.Value == frameRate
            && scene.SampleRate.Value == sampleRate
            && scene.ChannelLayout.Value == channelLayout
            && scene.MasterVolume.Value == masterVolume)
        {
            return false;
        }

        // These writes batch into one transaction, collapsing to a single history entry on Commit.
        scene.FrameSize.Value = frameSize;
        scene.Start.Value = start;
        scene.Duration.Value = duration;
        scene.FrameRate.Value = frameRate;
        scene.SampleRate.Value = sampleRate;
        scene.ChannelLayout.Value = channelLayout;
        scene.MasterVolume.Value = masterVolume;
        _historyManager.Commit(CommandNames.ChangeSceneSettings);
        return true;
    }
}

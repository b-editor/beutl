using Beutl.Editor.Operations;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.Editor.Services;

public sealed class SceneSettingsService : ISceneSettingsService
{
    private readonly HistoryManager _historyManager;

    public SceneSettingsService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool Apply(Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration)
        => Apply(scene, frameSize, start, duration, scene.FindHierarchicalParent<Project>().GetSpeakerLayout());

    public bool Apply(Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration, SpeakerLayout channelLayout)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Project? project = scene.FindHierarchicalParent<Project>();
        bool layoutChanged = project != null && project.ChannelLayout.Value != channelLayout;

        if (scene.FrameSize.Value == frameSize
            && scene.Start.Value == start
            && scene.Duration.Value == duration
            && !layoutChanged)
        {
            return false;
        }

        if (layoutChanged)
        {
            // The project is outside the observed scene; record its edit in the same transaction.
            SpeakerLayout previous = project!.ChannelLayout.Value;
            SetChannelLayout(project, channelLayout, out _);
            _historyManager.Record(new ChannelLayoutChangeOperation(project, previous, channelLayout)
            {
                SequenceNumber = _historyManager.GetNextSequenceNumber()
            });
        }

        // These writes batch into one transaction, collapsing to a single history entry on Commit.
        scene.FrameSize.Value = frameSize;
        scene.Start.Value = start;
        scene.Duration.Value = duration;
        _historyManager.Commit(CommandNames.ChangeSceneSettings);
        return true;
    }

    private static void SetChannelLayout(Project project, SpeakerLayout layout,
        out ChangeOperationFailureState failureState)
    {
        failureState = ChangeOperationFailureState.Unknown;
        SpeakerLayout previous = project.ChannelLayout.Value;
        project.ChannelLayout.Value = layout;
        if (project.Uri is { } uri)
        {
            try
            {
                ProjectPersistence.PersistOrRollback(() => CoreSerializer.StoreToUri(project, uri),
                    () => project.ChannelLayout.Value = previous);
            }
            catch (Exception ex) when (ex is not ProjectStateDivergedException)
            {
                // Only a persistence failure with a successful rollback is safe to retry.
                failureState = ChangeOperationFailureState.Unchanged;
                throw;
            }
        }
    }

    private sealed class ChannelLayoutChangeOperation(Project project, SpeakerLayout previous, SpeakerLayout next)
        : ChangeOperation
    {
        private ChangeOperationFailureState _failureState;

        public override ChangeOperationFailureState FailureState => _failureState;

        public override void Apply() => SetChannelLayout(project, next, out _failureState);

        public override void Revert() => SetChannelLayout(project, previous, out _failureState);
    }
}

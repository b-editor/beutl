using Beutl.Animation;

namespace Beutl.Editor.Services;

/// <summary>
/// Commits a batched <see cref="IKeyFrame.KeyTime"/> change. The InlineAnimation
/// view drives the <see cref="IKeyFrame"/> times directly while the user
/// drags, then hands the final values to this service so the history entry
/// is created in one place.
/// </summary>
public interface IKeyFrameMoveService
{
    void CommitMove(IReadOnlyList<IKeyFrame> keyFrames);
}

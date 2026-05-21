using Beutl.Animation;
using Beutl.Language;

namespace Beutl.Editor.Services;

public sealed class KeyFrameMoveService : IKeyFrameMoveService
{
    private readonly HistoryManager _historyManager;

    public KeyFrameMoveService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void CommitMove(IReadOnlyList<IKeyFrame> keyFrames)
    {
        ArgumentNullException.ThrowIfNull(keyFrames);
        if (keyFrames.Count == 0) return;

        _historyManager.Commit(CommandNames.MoveKeyFrame);
    }
}

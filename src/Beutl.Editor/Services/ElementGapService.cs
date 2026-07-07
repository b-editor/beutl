using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementGapService : IElementGapService
{
    private readonly HistoryManager _historyManager;

    public ElementGapService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool CloseGapAfter(Scene scene, Element anchor)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(anchor);

        if (!scene.CloseGapAfter(anchor)) return false;
        _historyManager.Commit(CommandNames.CloseGap);
        return true;
    }

    public int CloseAllGaps(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int closed = scene.CloseAllGaps();
        if (closed > 0) _historyManager.Commit(CommandNames.CloseAllGaps);
        return closed;
    }
}

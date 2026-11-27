using Beutl.Configuration;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementGapService : IElementGapService
{
    private readonly HistoryManager _historyManager;
    private readonly EditorConfig _configuration;

    public ElementGapService(HistoryManager historyManager, EditorConfig configuration)
    {
        _configuration = configuration;
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool CloseGapAfter(Scene scene, Element anchor)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(anchor);

        if (!scene.CloseGapAfter(anchor, _configuration.AutoAdjustSceneDuration.Value)) return false;
        _historyManager.Commit(CommandNames.CloseGap);
        return true;
    }

    public int CloseAllGaps(Scene scene)
        => CloseAllGaps(scene, null);

    internal int CloseAllGaps(Scene scene, IReadOnlySet<int>? layers)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int closed = scene.CloseAllGaps(layers, _configuration.AutoAdjustSceneDuration.Value);
        if (closed > 0) _historyManager.Commit(CommandNames.CloseAllGaps);
        return closed;
    }
}

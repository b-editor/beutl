using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementResizeService : IElementResizeService
{
    private readonly HistoryManager _historyManager;

    public ElementResizeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return;

        foreach (ElementResizeRequest req in requests)
        {
            scene.MoveChild(req.ZIndex, req.NewStart, req.NewLength, req.Element);
        }

        _historyManager.Commit(CommandNames.MoveElement);
    }
}

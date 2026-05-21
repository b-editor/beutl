using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class LayerMoveService : ILayerMoveService
{
    private readonly HistoryManager _historyManager;

    public LayerMoveService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public LayerMovePlan ApplyMove(
        Scene scene,
        int oldLayer,
        int newLayer,
        IReadOnlyList<Element> directElements)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(directElements);
        if (oldLayer == newLayer) return LayerMovePlan.Noop;

        // Enumerate the shift-range before mutating, so the range is
        // captured against the pre-write state.
        var shifted = new List<Element>();
        foreach (Element child in scene.Children)
        {
            if (child.ZIndex == oldLayer) continue;
            if (oldLayer < newLayer && child.ZIndex > oldLayer && child.ZIndex <= newLayer)
            {
                shifted.Add(child);
            }
            else if (oldLayer > newLayer && child.ZIndex < oldLayer && child.ZIndex >= newLayer)
            {
                shifted.Add(child);
            }
        }

        int shiftDelta = oldLayer < newLayer ? -1 : 1;
        foreach (Element e in directElements)
        {
            e.ZIndex = newLayer;
        }
        foreach (Element e in shifted)
        {
            e.ZIndex += shiftDelta;
        }

        _historyManager.Commit(CommandNames.MoveLayer);
        return new LayerMovePlan(oldLayer, newLayer, directElements, shifted);
    }
}

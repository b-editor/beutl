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
            if (InShiftRange(child.ZIndex, oldLayer, newLayer))
            {
                shifted.Add(child);
            }
        }

        // TimelineLayer carries the layer's persisted color / name and its own
        // recorded ZIndex. Its writes must land in the SAME transaction as the
        // Element.ZIndex writes; otherwise an Undo of MoveLayer reverts the
        // elements but leaves the header model desynced, and the stray writes
        // leak into the next history entry. Snapshot before mutating, like the
        // Element pass above.
        var directLayers = new List<TimelineLayer>();
        var shiftedLayers = new List<TimelineLayer>();
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.ZIndex == oldLayer)
            {
                directLayers.Add(layer);
            }
            else if (InShiftRange(layer.ZIndex, oldLayer, newLayer))
            {
                shiftedLayers.Add(layer);
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

        foreach (TimelineLayer layer in directLayers)
        {
            layer.ZIndex = newLayer;
        }
        foreach (TimelineLayer layer in shiftedLayers)
        {
            layer.ZIndex += shiftDelta;
        }

        _historyManager.Commit(CommandNames.MoveLayer);
        return new LayerMovePlan(oldLayer, newLayer, directElements, shifted);
    }

    // A ZIndex is shifted when it sits strictly between the old layer and the
    // new layer, inclusive of the new layer (the slot the moved layer vacates).
    private static bool InShiftRange(int zIndex, int oldLayer, int newLayer)
        => oldLayer < newLayer
            ? zIndex > oldLayer && zIndex <= newLayer
            : zIndex < oldLayer && zIndex >= newLayer;
}

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

        // A layer move rewrites the ZIndex of the source layer and everything it
        // shifts past; refuse the whole move if any locked layer or clip sits there,
        // otherwise locked content moves indirectly.
        if (AnyLockedInMoveRange(scene, oldLayer, newLayer)) return LayerMovePlan.Noop;

        // Snapshot the shift-range before mutating, against the pre-write state.
        var shifted = new List<Element>();
        foreach (Element child in scene.Children)
        {
            if (InShiftRange(child.ZIndex.Value, oldLayer, newLayer))
            {
                shifted.Add(child);
            }
        }

        // TimelineLayer's ZIndex writes must land in the SAME transaction as the
        // Element writes, or Undo desyncs the header model and the stray writes
        // leak into the next history entry. Snapshot first, like the pass above.
        var directLayers = new List<TimelineLayer>();
        var shiftedLayers = new List<TimelineLayer>();
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.ZIndex.Value == oldLayer)
            {
                directLayers.Add(layer);
            }
            else if (InShiftRange(layer.ZIndex.Value, oldLayer, newLayer))
            {
                shiftedLayers.Add(layer);
            }
        }

        int shiftDelta = oldLayer < newLayer ? -1 : 1;
        foreach (Element e in directElements)
        {
            e.ZIndex.Value = newLayer;
        }
        foreach (Element e in shifted)
        {
            e.ZIndex.Value += shiftDelta;
        }

        foreach (TimelineLayer layer in directLayers)
        {
            layer.ZIndex.Value = newLayer;
        }
        foreach (TimelineLayer layer in shiftedLayers)
        {
            layer.ZIndex.Value += shiftDelta;
        }

        // A layer reference follows the same permutation as the layer it names, including references
        // on clips outside the moved range. Keep these writes in the layer move's undo transaction.
        foreach (Element element in scene.Children)
        {
            if (element.MatteSourceKind.Value != MatteSourceKind.Layer
                || element.MatteSourceZIndex.Value is not { } source) continue;
            if (source == oldLayer)
                element.MatteSourceZIndex.Value = newLayer;
            else if (InShiftRange(source, oldLayer, newLayer))
                element.MatteSourceZIndex.Value = source + shiftDelta;
        }

        _historyManager.Commit(CommandNames.MoveLayer);
        return new LayerMovePlan(oldLayer, newLayer, directElements, shifted);
    }

    private static bool AnyLockedInMoveRange(Scene scene, int oldLayer, int newLayer)
    {
        foreach (Element child in scene.Children)
        {
            if ((child.ZIndex.Value == oldLayer || InShiftRange(child.ZIndex.Value, oldLayer, newLayer))
                && scene.IsElementLocked(child))
            {
                return true;
            }
        }

        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.IsLocked.Value
                && (layer.ZIndex.Value == oldLayer || InShiftRange(layer.ZIndex.Value, oldLayer, newLayer)))
            {
                return true;
            }
        }

        return false;
    }

    // A ZIndex shifts when it lies between old and new layer, inclusive of the
    // new layer (the slot the moved layer vacates).
    private static bool InShiftRange(int zIndex, int oldLayer, int newLayer)
        => oldLayer < newLayer
            ? zIndex > oldLayer && zIndex <= newLayer
            : zIndex < oldLayer && zIndex >= newLayer;
}

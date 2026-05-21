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

    public LayerMovePlan PlanMove(
        Scene scene,
        int oldLayer,
        int newLayer,
        IReadOnlyList<Element> directElements)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(directElements);
        if (oldLayer == newLayer) return LayerMovePlan.Noop;

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

        return new LayerMovePlan(oldLayer, newLayer, directElements, shifted);
    }

    public void CommitMove(LayerMovePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsNoop) return;

        _historyManager.Commit(CommandNames.MoveLayer);
    }
}

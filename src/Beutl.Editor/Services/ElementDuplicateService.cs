using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementDuplicateService : IElementDuplicateService
{
    private static readonly ILogger s_logger = Log.CreateLogger<ElementDuplicateService>();

    private readonly HistoryManager _historyManager;

    public ElementDuplicateService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public DuplicateOutcome DuplicateAtClickedPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        MediaTime clickedFrame,
        int clickedLayer)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sources);

        // Duplicating a locked clip would bypass the lock's editing freeze — the
        // same rule ElementMoveService applies to Alt+drag duplicates. Only scene
        // children are checked: PasteElementsAsync routes deserialized clipboard
        // elements here, and their source ZIndex must not pick up this scene's
        // layer locks.
        Element[] sourceArray = sources
            .Where(e => !scene.Children.Contains(e) || !scene.IsElementLocked(e))
            .ToArray();
        if (sourceArray.Length == 0) return DuplicateOutcome.Failed;

        Element[] regenerated;
        TimeRange seedRange;
        int minZIndex;
        MediaTime anchorStart;
        int anchorZIndex;
        try
        {
            // Regeneration can throw on a corrupt / unknown-plugin element, so
            // keep it inside the guarded region (like DuplicateAtPosition).
            ObjectRegenerator.Regenerate(sourceArray, out regenerated);
            (seedRange, minZIndex, _) = DuplicateHelper.ComputePlacementRange(regenerated);

            // Only rows a duplicate actually lands on matter — a sparse selection
            // (layers 0 and 2) must not be blocked by content or a lock on layer 1.
            // Snapshot once: the bounded spiral search probes these per candidate.
            var rowOffsets = regenerated.Select(r => r.ZIndex.Value - minZIndex).ToHashSet();
            HashSet<int> lockedRows = scene.Layers.Where(l => l.IsLocked.Value).Select(l => l.ZIndex.Value).ToHashSet();
            (anchorStart, anchorZIndex) = ElementPlacementHelper.FindFreeAnchor(
                scene, seedRange, rowOffsets, lockedRows, clickedFrame, clickedLayer);

            // FindFreeAnchor avoids locked rows, but its give-up fallback can
            // still land on one; never place content inside a locked layer.
            if (ElementPlacementHelper.AnyDestinationRowLocked(anchorZIndex, rowOffsets, lockedRows))
            {
                return DuplicateOutcome.Failed;
            }

            DuplicateHelper.PlaceDuplicates(scene, regenerated, sourceArray, anchorStart, anchorZIndex);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "DuplicateAtClickedPosition failed for {Count} elements.", sourceArray.Length);
            return DuplicateOutcome.Failed;
        }

        _historyManager.Commit(CommandNames.DuplicateElement);
        return new DuplicateOutcome(true, new TimeRange(anchorStart, seedRange.Duration), anchorZIndex);
    }

    public bool DuplicateAtPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        MediaTime anchorStart,
        int anchorZIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) return false;

        var sourceArray = sources.ToArray();

        try
        {
            ObjectRegenerator.Regenerate(sourceArray, out Element[] regenerated);
            DuplicateHelper.PlaceDuplicates(scene, regenerated, sourceArray, anchorStart, Math.Max(anchorZIndex, 0));
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "DuplicateAtPosition failed for {Count} elements.", sourceArray.Length);
            return false;
        }

        _historyManager.Commit(CommandNames.DuplicateElement);
        return true;
    }

    public bool WouldOverlap(IReadOnlyList<Element> sources, MediaTime anchorStart, int anchorZIndex)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return DuplicateHelper.WouldOverlapSources(sources, anchorStart, anchorZIndex);
    }
}

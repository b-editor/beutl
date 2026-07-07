using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementDuplicateService : IElementDuplicateService
{
    private const int MaxSearchSteps = 100_000;

    private static readonly ILogger s_logger = Log.CreateLogger<ElementDuplicateService>();

    private readonly HistoryManager _historyManager;

    public ElementDuplicateService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public DuplicateOutcome DuplicateAtClickedPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        TimeSpan clickedFrame,
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
        int minZIndex, maxZIndex;
        TimeSpan anchorStart;
        int anchorZIndex;
        try
        {
            // Regeneration can throw on a corrupt / unknown-plugin element, so
            // keep it inside the guarded region (like DuplicateAtPosition).
            ObjectRegenerator.Regenerate(sourceArray, out regenerated);
            (seedRange, minZIndex, maxZIndex) = DuplicateHelper.ComputePlacementRange(regenerated);
            (anchorStart, anchorZIndex) =
                CorrectPosition(scene, seedRange, minZIndex, maxZIndex, clickedFrame, clickedLayer);

            // CorrectPosition avoids locked rows, but its give-up fallback can
            // still land on one; never place content inside a locked layer.
            if (AnyLayerLockedInRange(scene, anchorZIndex, anchorZIndex + (maxZIndex - minZIndex)))
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
        TimeSpan anchorStart,
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

    public bool WouldOverlap(IReadOnlyList<Element> sources, TimeSpan anchorStart, int anchorZIndex)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return DuplicateHelper.WouldOverlapSources(sources, anchorStart, anchorZIndex);
    }

    /// <summary>
    /// Spiral search around the clicked position for a non-overlapping slot,
    /// bounded by <see cref="MaxSearchSteps"/> so a packed timeline can't hang.
    /// </summary>
    private static (TimeSpan Start, int ZIndex) CorrectPosition(
        Scene scene,
        TimeRange range,
        int minZIndex,
        int maxZIndex,
        TimeSpan clickedFrame,
        int clickedLayer)
    {
        int rate = SceneTimeRangeService.GetFrameRate(scene);
        TimeSpan step = TimeSpan.FromSeconds(1d / rate);
        TimeSpan length = range.Duration;
        int layerCount = maxZIndex - minZIndex + 1;

        TimeSpan newStart = clickedFrame;
        int newZIndex = clickedLayer;

        if (IsPlacementFree(scene, new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
        {
            return (newStart, newZIndex);
        }

        // 時計回り: 右→下→左→上
        int[] dx = [1, 0, -1, 0];
        int[] dz = [0, 1, 0, -1];

        int dir = 0;
        int stepLen = 1;
        int stepped = 0;
        int turnCount = 0;
        int searchSteps = 0;

        while (true)
        {
            newStart += TimeSpan.FromTicks(step.Ticks * dx[dir]);
            newZIndex += dz[dir];

            if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
            if (newZIndex < 0) newZIndex = 0;

            if (IsPlacementFree(scene, new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
            {
                return (newStart, newZIndex);
            }

            if (++searchSteps >= MaxSearchSteps)
            {
                s_logger.LogWarning(
                    "CorrectPosition gave up after {Steps} steps; using last candidate at start={Start}, zIndex={ZIndex}.",
                    searchSteps, newStart, newZIndex);
                return (newStart, newZIndex);
            }

            stepped++;
            if (stepped == stepLen)
            {
                stepped = 0;
                dir = (dir + 1) & 3;
                turnCount++;
                if ((turnCount & 1) == 0)
                    stepLen++;
            }
        }
    }

    private static bool IsPlacementFree(Scene scene, TimeRange range, int minZIndex, int maxZIndex)
        => !IsOverlapping(scene, range, minZIndex, maxZIndex)
           && !AnyLayerLockedInRange(scene, minZIndex, maxZIndex);

    private static bool AnyLayerLockedInRange(Scene scene, int minZIndex, int maxZIndex)
    {
        for (int z = minZIndex; z <= maxZIndex; z++)
        {
            if (scene.IsLayerLocked(z)) return true;
        }

        return false;
    }

    private static bool IsOverlapping(Scene scene, TimeRange range, int minZIndex, int maxZIndex)
    {
        foreach (Element child in scene.Children)
        {
            if (child.ZIndex < minZIndex || child.ZIndex > maxZIndex) continue;
            TimeRange other = child.Range;
            if (other == range || other.Intersects(range) || other.Contains(range) || range.Contains(other))
            {
                return true;
            }
        }

        return false;
    }
}

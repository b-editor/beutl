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
        if (sources.Count == 0) return DuplicateOutcome.Failed;

        var sourceArray = sources.ToArray();
        ObjectRegenerator.Regenerate(sourceArray, out Element[] regenerated);

        (TimeRange seedRange, int minZIndex, int maxZIndex) =
            DuplicateHelper.ComputePlacementRange(regenerated);
        (TimeSpan anchorStart, int anchorZIndex) =
            CorrectPosition(scene, seedRange, minZIndex, maxZIndex, clickedFrame, clickedLayer);

        try
        {
            DuplicateHelper.PlaceDuplicates(scene, regenerated, sourceArray, anchorStart, anchorZIndex);
        }
        catch (Exception)
        {
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
    /// Spiral search around the clicked position for a non-overlapping slot.
    /// Bounded by <see cref="MaxSearchSteps"/> so a densely-packed timeline
    /// can never hang the caller.
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

        if (!IsOverlapping(scene, new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
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

            if (!IsOverlapping(scene, new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
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

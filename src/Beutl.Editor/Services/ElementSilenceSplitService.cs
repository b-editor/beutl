using Beutl.Audio;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementSilenceSplitService : ISilenceSplitService
{
    private readonly HistoryManager _historyManager;
    private readonly ElementStructureService _structureService;

    public ElementSilenceSplitService(HistoryManager historyManager, ElementStructureService structureService)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _structureService = structureService ?? throw new ArgumentNullException(nameof(structureService));
    }

    public SilenceSplitOutcome SplitBySilence(
        Scene scene,
        IReadOnlyList<Element> targets,
        IReadOnlyList<SilenceRegion> silenceRegions,
        SilenceSplitMode mode)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(silenceRegions);
        if (targets.Count == 0 || silenceRegions.Count == 0 || scene.Uri is null)
            return SilenceSplitOutcome.None;

        int rate = SceneTimeRangeService.GetFrameRate(scene);
        TimeSpan minDuration = TimeSpan.FromSeconds(1d / rate);

        var boundaries = new SortedSet<TimeSpan>();
        foreach (SilenceRegion region in silenceRegions)
        {
            foreach (Element target in targets)
            {
                TimeSpan start = target.Start;
                TimeSpan end = target.Start + target.Length;
                TimeSpan s = ClampBoundary(region.Start, start, end);
                TimeSpan e = ClampBoundary(region.End, start, end);
                if (s > start && s < end) boundaries.Add(s.RoundToRate(rate));
                if (e > start && e < end) boundaries.Add(e.RoundToRate(rate));
            }
        }

        if (boundaries.Count == 0 && mode == SilenceSplitMode.SplitOnly)
            return SilenceSplitOutcome.None;

        int splitCount = 0;
        int deletedCount = 0;
        foreach (Element target in targets)
        {
            var pieces = new List<Element> { target };
            foreach (TimeSpan boundary in boundaries)
            {
                int idx = IndexOfPieceContaining(pieces, boundary);
                if (idx < 0) continue;
                List<Element> created = _structureService.SplitCore(scene, [pieces[idx]], boundary);
                if (created.Count == 0) continue;
                splitCount++;
                pieces.Insert(idx + 1, created[0]);
            }

            if (mode == SilenceSplitMode.SplitAndDeleteSilence)
            {
                var toDelete = new List<Element>();
                foreach (Element piece in pieces)
                {
                    if (IsSilencePiece(piece, silenceRegions, minDuration))
                        toDelete.Add(piece);
                }
                if (toDelete.Count > 0)
                {
                    ElementStructureService.DeleteCore(scene, toDelete);
                    deletedCount += toDelete.Count;
                }
            }
        }

        if (splitCount == 0 && deletedCount == 0) return SilenceSplitOutcome.None;

        _historyManager.Commit(CommandNames.AutoSplitBySilence);
        return new SilenceSplitOutcome(splitCount, deletedCount);
    }

    private static TimeSpan ClampBoundary(TimeSpan boundary, TimeSpan start, TimeSpan end)
    {
        if (boundary < start) return start;
        if (boundary > end) return end;
        return boundary;
    }

    private static int IndexOfPieceContaining(List<Element> pieces, TimeSpan boundary)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].Range.Contains(boundary))
                return i;
        }
        return -1;
    }

    private static bool IsSilencePiece(Element piece, IReadOnlyList<SilenceRegion> regions, TimeSpan tolerance)
    {
        TimeSpan start = piece.Start;
        TimeSpan end = piece.Start + piece.Length;
        foreach (SilenceRegion r in regions)
        {
            if (start >= r.Start - tolerance && end <= r.End + tolerance)
                return true;
        }
        return false;
    }
}

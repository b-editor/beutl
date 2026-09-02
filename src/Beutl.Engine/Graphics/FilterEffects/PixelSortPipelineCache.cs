namespace Beutl.Graphics.Effects;

internal sealed class PixelSortPipelineCache<TPipeline>
    where TPipeline : class
{
    private readonly object _sync = new();
    private readonly Func<PixelSortKey, TPipeline> _createPrepare;
    private readonly Func<PixelSortDirection, TPipeline> _createRank;
    private readonly Func<PixelSortDirection, bool, TPipeline> _createGather;
    private readonly Slot[] _prepareSlots = new Slot[6];
    private readonly Slot[] _rankSlots = new Slot[2];
    private readonly Slot[] _gatherSlots = new Slot[4];

    public PixelSortPipelineCache(
        Func<PixelSortKey, TPipeline> createPrepare,
        Func<PixelSortDirection, TPipeline> createRank,
        Func<PixelSortDirection, bool, TPipeline> createGather)
    {
        _createPrepare = createPrepare;
        _createRank = createRank;
        _createGather = createGather;
    }

    public PixelSortPipelines<TPipeline>? GetOrCreate(
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        int prepareIndex = GetSortKeyIndex(sortKey);
        int rankIndex = GetDirectionIndex(direction);
        int gatherIndex = (rankIndex * 2) + (ascending ? 1 : 0);

        lock (_sync)
        {
            ref Slot prepareSlot = ref _prepareSlots[prepareIndex];
            // Publish a slot only after its factory succeeds. Pipeline creation can fail for transient
            // device or resource reasons that are indistinguishable here from deterministic validation
            // failures, so an exception deliberately leaves the slot empty for the next invocation to retry.
            prepareSlot.Value ??= _createPrepare(sortKey);

            if (prepareSlot.Value is not { } prepare)
                return null;

            ref Slot rankSlot = ref _rankSlots[rankIndex];
            rankSlot.Value ??= _createRank(direction);

            if (rankSlot.Value is not { } rank)
                return null;

            ref Slot gatherSlot = ref _gatherSlots[gatherIndex];
            gatherSlot.Value ??= _createGather(direction, ascending);

            return gatherSlot.Value is { } gather
                ? new PixelSortPipelines<TPipeline>(prepare, rank, gather)
                : null;
        }
    }

    private static int GetDirectionIndex(PixelSortDirection direction)
        => direction switch
        {
            PixelSortDirection.Horizontal => 0,
            PixelSortDirection.Vertical => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

    private static int GetSortKeyIndex(PixelSortKey sortKey)
        => sortKey switch
        {
            PixelSortKey.Luminance => 0,
            PixelSortKey.Hue => 1,
            PixelSortKey.Saturation => 2,
            PixelSortKey.Red => 3,
            PixelSortKey.Green => 4,
            PixelSortKey.Blue => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, null),
        };

    private struct Slot
    {
        public TPipeline? Value;
    }
}

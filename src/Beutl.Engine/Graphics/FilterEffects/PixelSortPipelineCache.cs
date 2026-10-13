namespace Beutl.Graphics.Effects;

internal sealed class PixelSortPipelineCache<TPipeline>
    where TPipeline : class
{
    private readonly object _sync = new();
    private readonly Func<PixelSortKey, TPipeline> _createPrepare;
    private readonly Func<PixelSortDirection, TPipeline> _createRank;
    private readonly Func<PixelSortDirection, bool, TPipeline> _createGather;
    private readonly TPipeline?[] _prepareSlots = new TPipeline?[6];
    private readonly TPipeline?[] _rankSlots = new TPipeline?[2];
    private readonly TPipeline?[] _gatherSlots = new TPipeline?[4];

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
            // Publish a slot only after its factory succeeds. Pipeline creation can fail for transient
            // device or resource reasons that are indistinguishable here from deterministic validation
            // failures, so an exception deliberately leaves the slot empty for the next invocation to retry.
            _prepareSlots[prepareIndex] ??= _createPrepare(sortKey);

            if (_prepareSlots[prepareIndex] is not { } prepare)
                return null;

            _rankSlots[rankIndex] ??= _createRank(direction);

            if (_rankSlots[rankIndex] is not { } rank)
                return null;

            _gatherSlots[gatherIndex] ??= _createGather(direction, ascending);

            return _gatherSlots[gatherIndex] is { } gather
                ? new PixelSortPipelines<TPipeline>(prepare, rank, gather)
                : null;
        }
    }

    private static int GetDirectionIndex(PixelSortDirection direction)
        => Enum.IsDefined(direction)
            ? (int)direction
            : throw new ArgumentOutOfRangeException(nameof(direction), direction, null);

    private static int GetSortKeyIndex(PixelSortKey sortKey)
        => Enum.IsDefined(sortKey)
            ? (int)sortKey
            : throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, null);
}

internal readonly record struct PixelSortPipelines<TPipeline>(
    TPipeline Prepare,
    TPipeline Rank,
    TPipeline Gather)
    where TPipeline : class;

using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderCacheResolution
{
    private readonly RenderRequestId _requestId;
    private readonly int[] _selectedDecisionIndices;

    public RenderCacheResolution(ImmutableArray<RenderCacheDecision> decisions)
    {
        if (decisions.IsDefault)
            throw new ArgumentException("Render-cache decisions must be initialized.", nameof(decisions));

        Decisions = decisions;
        int hitCount = 0;
        int missCaptureCount = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            RenderCacheDecision decision = decisions[index];
            ArgumentNullException.ThrowIfNull(decision.Candidate);
            RenderCacheCandidateId id = decision.Candidate.Id;
            if (id.Value != index + 1L
                || decision.Candidate.FragmentId.RequestId != id.RequestId
                || index > 0 && id.RequestId != _requestId)
            {
                throw new ArgumentException(
                    "Render-cache decisions must follow their request's dense candidate order.",
                    nameof(decisions));
            }
            if (index == 0)
                _requestId = id.RequestId;

            switch (decision.Kind)
            {
                case RenderCacheResolutionKind.Hit:
                    if (decision.MissIdentity is not null || decision.HitEntry is null)
                    {
                        throw new ArgumentException("A cache hit requires its identity and entry.", nameof(decisions));
                    }
                    hitCount++;
                    break;
                case RenderCacheResolutionKind.MissCapture:
                    if (decision.MissIdentity is null || decision.HitEntry is not null)
                        throw new ArgumentException("A cache miss capture requires only its identity.", nameof(decisions));
                    missCaptureCount++;
                    break;
                case RenderCacheResolutionKind.Bypass:
                case RenderCacheResolutionKind.Superseded:
                    if (decision.MissIdentity is not null || decision.HitEntry is not null)
                        throw new ArgumentException("Only a selected cache decision may carry an outcome.", nameof(decisions));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(decisions), "A cache decision has an invalid kind.");
            }
        }

        MissCaptureCount = missCaptureCount;
        _selectedDecisionIndices = hitCount + missCaptureCount == 0
            ? []
            : new int[hitCount + missCaptureCount];
        int selected = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            if (decisions[index].Kind is RenderCacheResolutionKind.Hit or RenderCacheResolutionKind.MissCapture)
                _selectedDecisionIndices[selected++] = index;
        }

        if (_selectedDecisionIndices.Length > 1)
            Array.Sort(_selectedDecisionIndices, new ProducerDecisionComparer(decisions));
        long previousProducer = long.MinValue;
        bool producerHasHit = false;
        for (int index = 0; index < _selectedDecisionIndices.Length; index++)
        {
            RenderCacheDecision decision = decisions[_selectedDecisionIndices[index]];
            long producer = ProducerValue(decision);
            if (producer != previousProducer)
            {
                previousProducer = producer;
                producerHasHit = false;
            }
            if (decision.Kind == RenderCacheResolutionKind.Hit && producerHasHit)
            {
                throw new ArgumentException(
                    "One fragment cannot have more than one selected cache hit.",
                    nameof(decisions));
            }
            producerHasHit |= decision.Kind == RenderCacheResolutionKind.Hit;
        }
    }

    public ImmutableArray<RenderCacheDecision> Decisions { get; }

    public int MissCaptureCount { get; }

    public bool TryGetHit(RenderFragmentId producerId, out RenderCacheDecision decision)
    {
        int selectedIndex = FindFirst(producerId);
        if (selectedIndex >= 0)
        {
            RenderCacheDecision selected = Decisions[_selectedDecisionIndices[selectedIndex]];
            if (selected.Kind == RenderCacheResolutionKind.Hit)
            {
                decision = selected;
                return true;
            }
        }

        decision = default;
        return false;
    }

    public bool HasHitProducer(RenderFragmentId producerId)
        => TryGetHit(producerId, out _);

    public bool HasMissCaptureProducer(RenderFragmentId producerId)
        => !GetMissCaptureDecisionIndices(producerId).IsEmpty;

    public bool HasSelectedProducer(RenderFragmentId producerId)
        => FindFirst(producerId) >= 0;

    public ReadOnlySpan<int> GetMissCaptureDecisionIndices(RenderFragmentId producerId)
    {
        int start = FindFirst(producerId);
        if (start < 0)
            return [];

        while (start < _selectedDecisionIndices.Length)
        {
            RenderCacheDecision decision = Decisions[_selectedDecisionIndices[start]];
            if (decision.Candidate.FragmentId != producerId)
                return [];
            if (decision.Kind == RenderCacheResolutionKind.MissCapture)
                break;
            start++;
        }
        int end = start;
        while (end < _selectedDecisionIndices.Length)
        {
            RenderCacheDecision decision = Decisions[_selectedDecisionIndices[end]];
            if (decision.Candidate.FragmentId != producerId
                || decision.Kind != RenderCacheResolutionKind.MissCapture)
            {
                break;
            }
            end++;
        }
        return _selectedDecisionIndices.AsSpan(start, end - start);
    }

    private int FindFirst(RenderFragmentId producerId)
    {
        if (producerId.RequestId != _requestId)
            return -1;

        int low = 0;
        int high = _selectedDecisionIndices.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (ProducerValue(Decisions[_selectedDecisionIndices[middle]]) < producerId.Value)
                low = middle + 1;
            else
                high = middle;
        }

        return low < _selectedDecisionIndices.Length
               && ProducerValue(Decisions[_selectedDecisionIndices[low]]) == producerId.Value
            ? low
            : -1;
    }

    private static long ProducerValue(RenderCacheDecision decision)
        => decision.Candidate.FragmentId.Value;

    private sealed class ProducerDecisionComparer(ImmutableArray<RenderCacheDecision> decisions) : IComparer<int>
    {
        public int Compare(int leftIndex, int rightIndex)
        {
            RenderCacheDecision left = decisions[leftIndex];
            RenderCacheDecision right = decisions[rightIndex];
            int producer = ProducerValue(left).CompareTo(ProducerValue(right));
            if (producer != 0)
                return producer;
            int kind = left.Kind.CompareTo(right.Kind);
            return kind != 0
                ? kind
                : left.Candidate.Id.Value.CompareTo(right.Candidate.Id.Value);
        }
    }
}

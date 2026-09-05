namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Retains the last structural request family for a renderer. Each stable depth-first family slot keeps
/// one graph-independent index plan, reused only when the complete structural identity compares equal.
/// </summary>
internal sealed class StructuralPlanCache : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private long _hits;
    private long _misses;
    private long _compilations;
    private long _replacements;
    private bool _disposed;

    public StructuralPlanCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new StructuralPlanCacheStatistics(
                    _hits,
                    _misses,
                    _compilations,
                    _replacements,
                    _entries.Count);
            }
        }
    }

    public ExecutionIslandPlan GetOrCompile<TState>(
        StructuralPlanIdentity identity,
        TState state,
        Func<TState, ExecutionIslandPlan> compile,
        int familySlot = 0)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentOutOfRangeException.ThrowIfNegative(familySlot);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (familySlot > _entries.Count)
            {
                throw new InvalidOperationException(
                    "Structural-plan family slots must be requested in depth-first order.");
            }

            bool replacing = familySlot < _entries.Count;
            if (replacing && _entries[familySlot].Identity.Equals(identity))
            {
                _hits++;
                return _entries[familySlot].Plan;
            }

            _misses++;
            ExecutionIslandPlan compiled = compile(state);
            if (replacing)
            {
                _replacements++;
                _entries[familySlot] = new Entry(identity, compiled);
            }
            else
            {
                _entries.Add(new Entry(identity, compiled));
            }
            _compilations++;
            return compiled;
        }
    }

    public void RetainFamilySlots(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (count >= _entries.Count)
                return;
            _entries.RemoveRange(count, _entries.Count - count);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entries.Clear();
        }
    }

    private readonly record struct Entry(
        StructuralPlanIdentity Identity,
        ExecutionIslandPlan Plan);
}

internal readonly record struct StructuralCacheBoundaryIdentity(
    int FragmentIndex,
    RenderCacheResolutionKind Kind);

using System.Runtime.ExceptionServices;

using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Renderer-owned cache for compiled shader programs. The merged-program hash selects a bucket only; exact
/// <see cref="ShaderProgramIdentity"/> and backend-context equality select an entry. Mutable programs use
/// exclusive leases, while immutable programs may opt into shared leases.
/// </summary>
internal sealed class ProgramCache<TProgram> : IDisposable
    where TProgram : class, IDisposable
{
    private readonly object _gate = new();
    private readonly Action<TProgram> _resetRuntimeBindings;
    private readonly Func<TProgram, long> _retainedByteSize;
    private readonly long _maxRetainedBytes;
    private readonly bool _shareLeasedPrograms;
    private readonly Dictionary<int, List<Entry>> _buckets = [];
    private readonly Dictionary<object, object> _activeContexts = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _retainedBytes;
    private long _hits;
    private long _misses;
    private long _creations;
    private long _evictions;
    private ExceptionDispatchInfo? _deferredCleanupFailure;
    private bool _disposed;

    public ProgramCache(
        Action<TProgram> resetRuntimeBindings,
        Func<TProgram, long> retainedByteSize,
        long maxRetainedBytes,
        bool shareLeasedPrograms = false)
    {
        _resetRuntimeBindings = resetRuntimeBindings
            ?? throw new ArgumentNullException(nameof(resetRuntimeBindings));
        _retainedByteSize = retainedByteSize
            ?? throw new ArgumentNullException(nameof(retainedByteSize));
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedBytes);
        _maxRetainedBytes = maxRetainedBytes;
        _shareLeasedPrograms = shareLeasedPrograms;
    }

    public ProgramCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new ProgramCacheStatistics(
                    _hits,
                    _misses,
                    _creations,
                    _evictions,
                    _lru.Count,
                    _retainedBytes);
            }
        }
    }

    /// <summary>
    /// Finds or creates a program for a merged source. Runtime-only values are deliberately absent from the key and
    /// are cleared before the lease is returned and again when it is discharged.
    /// </summary>
    public ProgramCacheLease<TProgram> GetOrCreate(
        SkslMergedProgram program,
        ProgramCacheContextKey context,
        Func<SkslMergedProgram, TProgram> create)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(create);
        return GetOrCreate(program.Identity, context, program, create);
    }

    /// <summary>
    /// Finds or creates a program by its complete merged identity. This overload is also the collision-test seam:
    /// callers may construct identities with a forced bucket hash while equality still compares full source and
    /// binding signature.
    /// </summary>
    public ProgramCacheLease<TProgram> GetOrCreate(
        ShaderProgramIdentity identity,
        ProgramCacheContextKey context,
        Func<TProgram> create)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(create);
        return GetOrCreateCore(
            identity,
            context,
            create,
            static factory => factory());
    }

    /// <summary>
    /// Finds or creates a program using an explicit factory state. A static factory keeps warmed lookups from
    /// allocating a capturing closure.
    /// </summary>
    public ProgramCacheLease<TProgram> GetOrCreate<TState>(
        ShaderProgramIdentity identity,
        ProgramCacheContextKey context,
        TState factoryState,
        Func<TState, TProgram> create)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(create);
        return GetOrCreateCore(identity, context, factoryState, create);
    }

    private ProgramCacheLease<TProgram> GetOrCreateCore<TState>(
        ShaderProgramIdentity identity,
        ProgramCacheContextKey context,
        TState factoryState,
        Func<TState, TProgram> create)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Entry? entry = FindEntry(identity, context);
            if (entry is not null)
            {
                _hits++;
                Touch(entry);
                if (_shareLeasedPrograms || !entry.IsLeased)
                {
                    if (!_shareLeasedPrograms)
                    {
                        try
                        {
                            _resetRuntimeBindings(entry.Program);
                        }
                        catch (Exception ex)
                        {
                            RemoveEntry(entry, countEviction: true);
                            RecordCleanupFailure(DisposeProgramsBestEffort([entry.Program]));
                            ExceptionDispatchInfo.Capture(ex).Throw();
                            throw;
                        }
                    }

                    entry.LeaseCount++;
                    return new ProgramCacheLease<TProgram>(
                        this,
                        entry,
                        entry.Program,
                        isCacheHit: true,
                        isTransient: false);
                }

                TProgram reentrant = CreateResetProgram(factoryState, create, out _);
                return new ProgramCacheLease<TProgram>(
                    this,
                    entry: null,
                    reentrant,
                    isCacheHit: true,
                    isTransient: true);
            }

            _misses++;
            TProgram created = CreateResetProgram(factoryState, create, out long retainedBytes);
            if (_maxRetainedBytes == 0 || retainedBytes > _maxRetainedBytes)
            {
                return new ProgramCacheLease<TProgram>(
                    this,
                    entry: null,
                    created,
                    isCacheHit: false,
                    isTransient: true);
            }

            var inserted = new Entry(identity, context, created, retainedBytes)
            {
                LeaseCount = 1,
            };
            inserted.LruNode = _lru.AddFirst(inserted);
            if (!_buckets.TryGetValue(identity.BucketHash, out List<Entry>? bucket))
            {
                bucket = [];
                _buckets.Add(identity.BucketHash, bucket);
            }

            bucket.Add(inserted);
            _retainedBytes = checked(_retainedBytes + retainedBytes);
            List<TProgram> evicted = TrimToBudget();
            RecordCleanupFailure(DisposeProgramsBestEffort(evicted));
            return new ProgramCacheLease<TProgram>(
                this,
                inserted,
                inserted.Program,
                isCacheHit: false,
                isTransient: false);
        }
    }

    /// <summary>
    /// Invalidates every program compiled for one context. Leased programs are detached immediately and disposed
    /// only when their outer lease is returned.
    /// </summary>
    public int EvictContext(object deviceIdentity, object contextIdentity)
    {
        ArgumentNullException.ThrowIfNull(deviceIdentity);
        ArgumentNullException.ThrowIfNull(contextIdentity);
        return EvictWhere(context =>
            Equals(context.DeviceIdentity, deviceIdentity)
            && Equals(context.ContextIdentity, contextIdentity));
    }

    /// <summary>
    /// Sets the active context for one cache-owned device domain and evicts entries from its preceding context.
    /// Leased entries are detached immediately and disposed after their last lease returns.
    /// </summary>
    public int SynchronizeContext(object deviceIdentity, object contextIdentity)
    {
        ArgumentNullException.ThrowIfNull(deviceIdentity);
        ArgumentNullException.ThrowIfNull(contextIdentity);
        List<TProgram> disposable;
        int count;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeContexts.TryGetValue(deviceIdentity, out object? current)
                && Equals(current, contextIdentity))
            {
                return 0;
            }

            _activeContexts[deviceIdentity] = contextIdentity;
            Entry[] matches = _lru
                .Where(entry =>
                    Equals(entry.Context.DeviceIdentity, deviceIdentity)
                    && !Equals(entry.Context.ContextIdentity, contextIdentity))
                .ToArray();
            count = matches.Length;
            disposable = new List<TProgram>(count);
            foreach (Entry entry in matches)
            {
                RemoveEntry(entry, countEviction: true);
                if (!entry.IsLeased)
                    disposable.Add(entry.Program);
            }
        }

        DisposeProgramsBestEffort(disposable)?.Throw();
        return count;
    }

    /// <summary>
    /// Invalidates every program compiled for one device, including all of its context generations.
    /// </summary>
    public int EvictDevice(object deviceIdentity)
    {
        ArgumentNullException.ThrowIfNull(deviceIdentity);
        return EvictWhere(context => Equals(context.DeviceIdentity, deviceIdentity));
    }

    public void Dispose()
    {
        List<TProgram> disposable;
        ExceptionDispatchInfo? firstFailure;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _activeContexts.Clear();
            Entry[] entries = [.. _lru];
            disposable = new List<TProgram>(entries.Length);
            foreach (Entry entry in entries)
            {
                RemoveEntry(entry, countEviction: true);
                if (!entry.IsLeased)
                    disposable.Add(entry.Program);
            }

            firstFailure = _deferredCleanupFailure;
            _deferredCleanupFailure = null;
        }

        ExceptionDispatchInfo? disposalFailure = DisposeProgramsBestEffort(disposable);
        (firstFailure ?? disposalFailure)?.Throw();
    }

    internal void Release(Entry? entry, TProgram program)
    {
        ExceptionDispatchInfo? primaryFailure = null;
        if (!_shareLeasedPrograms)
        {
            try
            {
                _resetRuntimeBindings(program);
            }
            catch (Exception ex)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(ex);
            }
        }

        List<TProgram> disposable = [];
        lock (_gate)
        {
            if (entry is null)
            {
                disposable.Add(program);
            }
            else
            {
                if (!ReferenceEquals(entry.Program, program) || entry.LeaseCount <= 0)
                {
                    throw new InvalidOperationException(
                        "A program-cache lease does not match an active cached checkout.");
                }

                entry.LeaseCount--;
                if (primaryFailure is not null)
                {
                    if (!entry.IsEvicted)
                        RemoveEntry(entry, countEviction: true);
                    if (!entry.IsLeased)
                        disposable.Add(program);
                }
                else if (!entry.IsLeased && (entry.IsEvicted || _disposed))
                {
                    if (!entry.IsEvicted)
                        RemoveEntry(entry, countEviction: true);
                    disposable.Add(program);
                }
                else if (!entry.IsLeased)
                {
                    disposable.AddRange(TrimToBudget());
                }
            }
        }

        ExceptionDispatchInfo? disposalFailure = DisposeProgramsBestEffort(disposable);
        if (primaryFailure is not null)
        {
            if (disposalFailure is not null)
            {
                lock (_gate)
                    RecordCleanupFailure(disposalFailure);
            }

            primaryFailure.Throw();
        }

        disposalFailure?.Throw();
    }

    private TProgram CreateResetProgram<TState>(
        TState factoryState,
        Func<TState, TProgram> create,
        out long retainedBytes)
    {
        TProgram program = create(factoryState)
            ?? throw new InvalidOperationException("The program factory returned null.");
        _creations++;
        try
        {
            _resetRuntimeBindings(program);
            retainedBytes = _retainedByteSize(program);
            if (retainedBytes <= 0)
            {
                throw new InvalidOperationException(
                    "A compiled program must report a positive retained byte size.");
            }

            return program;
        }
        catch (Exception ex)
        {
            RecordCleanupFailure(DisposeProgramsBestEffort([program]));
            ExceptionDispatchInfo.Capture(ex).Throw();
            throw;
        }
    }

    private Entry? FindEntry(
        ShaderProgramIdentity identity,
        ProgramCacheContextKey context)
    {
        if (!_buckets.TryGetValue(identity.BucketHash, out List<Entry>? bucket))
            return null;

        foreach (Entry candidate in bucket)
        {
            if (!candidate.IsEvicted
                && candidate.Identity.Equals(identity)
                && candidate.Context.Equals(context))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Touch(Entry entry)
    {
        LinkedListNode<Entry>? node = entry.LruNode;
        if (node is null || ReferenceEquals(_lru.First, node))
            return;

        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private List<TProgram> TrimToBudget()
    {
        var disposable = new List<TProgram>();
        while (_retainedBytes > _maxRetainedBytes)
        {
            LinkedListNode<Entry>? candidate = _lru.Last;
            while (candidate is not null && candidate.Value.IsLeased)
                candidate = candidate.Previous;
            if (candidate is null)
                break;

            Entry entry = candidate.Value;
            RemoveEntry(entry, countEviction: true);
            disposable.Add(entry.Program);
        }

        return disposable;
    }

    private int EvictWhere(Func<ProgramCacheContextKey, bool> predicate)
    {
        List<TProgram> disposable;
        int count;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Entry[] matches = _lru.Where(entry => predicate(entry.Context)).ToArray();
            count = matches.Length;
            disposable = new List<TProgram>(count);
            foreach (Entry entry in matches)
            {
                RemoveEntry(entry, countEviction: true);
                if (!entry.IsLeased)
                    disposable.Add(entry.Program);
            }
        }

        DisposeProgramsBestEffort(disposable)?.Throw();
        return count;
    }

    private void RemoveEntry(Entry entry, bool countEviction)
    {
        if (entry.IsEvicted)
            return;

        entry.IsEvicted = true;
        if (entry.LruNode is not null)
        {
            _lru.Remove(entry.LruNode);
            entry.LruNode = null;
        }

        if (_buckets.TryGetValue(entry.Identity.BucketHash, out List<Entry>? bucket))
        {
            bucket.Remove(entry);
            if (bucket.Count == 0)
                _buckets.Remove(entry.Identity.BucketHash);
        }

        _retainedBytes -= entry.RetainedBytes;
        if (countEviction)
            _evictions++;
    }

    private void RecordCleanupFailure(ExceptionDispatchInfo? failure)
    {
        if (failure is not null && _deferredCleanupFailure is null)
            _deferredCleanupFailure = failure;
    }

    private static ExceptionDispatchInfo? DisposeProgramsBestEffort(IEnumerable<TProgram> programs)
    {
        ExceptionDispatchInfo? firstFailure = null;
        foreach (TProgram program in programs)
        {
            try
            {
                program.Dispose();
            }
            catch (Exception ex)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        return firstFailure;
    }

    internal sealed class Entry(
        ShaderProgramIdentity identity,
        ProgramCacheContextKey context,
        TProgram program,
        long retainedBytes)
    {
        public ShaderProgramIdentity Identity { get; } = identity;

        public ProgramCacheContextKey Context { get; } = context;

        public TProgram Program { get; } = program;

        public long RetainedBytes { get; } = retainedBytes;

        public LinkedListNode<Entry>? LruNode { get; set; }

        public int LeaseCount { get; set; }

        public bool IsLeased => LeaseCount != 0;

        public bool IsEvicted { get; set; }
    }
}

using System.Runtime.ExceptionServices;

using Beutl.Graphics.Effects;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Identifies the backend lifetime and compile contract in which a merged shader program is valid.
/// </summary>
internal sealed class ProgramCacheContextKey : IEquatable<ProgramCacheContextKey>
{
    public ProgramCacheContextKey(
        object deviceIdentity,
        object contextIdentity,
        object backendCapabilityClass,
        string colorAlphaFormatContract,
        object compileOptionsIdentity)
    {
        DeviceIdentity = deviceIdentity ?? throw new ArgumentNullException(nameof(deviceIdentity));
        ContextIdentity = contextIdentity ?? throw new ArgumentNullException(nameof(contextIdentity));
        BackendCapabilityClass = backendCapabilityClass
            ?? throw new ArgumentNullException(nameof(backendCapabilityClass));
        ColorAlphaFormatContract = colorAlphaFormatContract
            ?? throw new ArgumentNullException(nameof(colorAlphaFormatContract));
        CompileOptionsIdentity = compileOptionsIdentity
            ?? throw new ArgumentNullException(nameof(compileOptionsIdentity));
    }

    public object DeviceIdentity { get; }

    public object ContextIdentity { get; }

    public object BackendCapabilityClass { get; }

    public string ColorAlphaFormatContract { get; }

    public object CompileOptionsIdentity { get; }

    public bool Equals(ProgramCacheContextKey? other)
        => other is not null
           && Equals(DeviceIdentity, other.DeviceIdentity)
           && Equals(ContextIdentity, other.ContextIdentity)
           && Equals(BackendCapabilityClass, other.BackendCapabilityClass)
           && string.Equals(
               ColorAlphaFormatContract,
               other.ColorAlphaFormatContract,
               StringComparison.Ordinal)
           && Equals(CompileOptionsIdentity, other.CompileOptionsIdentity);

    public override bool Equals(object? obj)
        => obj is ProgramCacheContextKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            DeviceIdentity,
            ContextIdentity,
            BackendCapabilityClass,
            ColorAlphaFormatContract,
            CompileOptionsIdentity);
}

internal readonly record struct ProgramCacheStatistics(
    long Hits,
    long Misses,
    long Creations,
    long Evictions,
    int RetainedPrograms,
    long RetainedBytes);

/// <summary>
/// Owns one backend-validated immutable runtime effect. Runtime values live in fresh
/// <see cref="SKRuntimeEffectUniforms"/> and <see cref="SKRuntimeEffectChildren"/> collections for each
/// execution lease, so no binding can leak between frames. A runtime builder cannot be used here because
/// disposing it also disposes the supplied effect.
/// </summary>
internal sealed class CachedSkRuntimeEffect : IDisposable
{
    private CachedSkRuntimeEffect(SKRuntimeEffect effect, int retainedBytes)
    {
        Effect = effect;
        RetainedBytes = retainedBytes;
    }

    public SKRuntimeEffect Effect { get; }

    public int RetainedBytes { get; }

    public static CachedSkRuntimeEffect Create(SkslMergedProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? errorText);
        if (effect is null || !string.IsNullOrWhiteSpace(errorText))
        {
            effect?.Dispose();
            throw new InvalidOperationException(
                $"SkSL program validation failed: {errorText ?? "the backend returned no program"}");
        }

        return new CachedSkRuntimeEffect(effect, Math.Max(1, program.SourceByteCount));
    }

    public void Dispose() => Effect.Dispose();
}

internal static class SkRuntimeEffectProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;

    public static ProgramCache<CachedSkRuntimeEffect> Create()
        => new(
            resetRuntimeBindings: static _ => { },
            retainedByteSize: static program => program.RetainedBytes,
            maxRetainedBytes: DefaultRetainedByteBudget);
}

/// <summary>
/// A single mutable-program checkout. Cached instances are exclusive; a re-entrant checkout of the same full key
/// receives a reset transient instance so it cannot overwrite the outer checkout's runtime bindings.
/// </summary>
internal sealed class ProgramCacheLease<TProgram> : IDisposable
    where TProgram : class, IDisposable
{
    private ProgramCache<TProgram>? _owner;
    private TProgram? _program;

    internal ProgramCacheLease(
        ProgramCache<TProgram> owner,
        ProgramCache<TProgram>.Entry? entry,
        TProgram program,
        bool isCacheHit,
        bool isTransient)
    {
        _owner = owner;
        Entry = entry;
        _program = program;
        IsCacheHit = isCacheHit;
        IsTransient = isTransient;
    }

    internal ProgramCache<TProgram>.Entry? Entry { get; }

    public TProgram Program
        => _program ?? throw new ObjectDisposedException(nameof(ProgramCacheLease<TProgram>));

    public bool IsCacheHit { get; }

    public bool IsTransient { get; }

    public void Dispose()
    {
        ProgramCache<TProgram>? owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
            return;

        TProgram program = Interlocked.Exchange(ref _program, null)
            ?? throw new InvalidOperationException("A program-cache lease lost its checked-out program.");
        owner.Release(Entry, program);
    }
}

/// <summary>
/// Renderer-owned cache for mutable compiled shader programs. The merged-program hash selects a bucket only;
/// exact <see cref="SkslMergedProgramIdentity"/> and backend-context equality select an entry.
/// </summary>
internal sealed class ProgramCache<TProgram> : IDisposable
    where TProgram : class, IDisposable
{
    private readonly object _gate = new();
    private readonly Action<TProgram> _resetRuntimeBindings;
    private readonly Func<TProgram, long> _retainedByteSize;
    private readonly long _maxRetainedBytes;
    private readonly Dictionary<int, List<Entry>> _buckets = [];
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
        long maxRetainedBytes)
    {
        _resetRuntimeBindings = resetRuntimeBindings
            ?? throw new ArgumentNullException(nameof(resetRuntimeBindings));
        _retainedByteSize = retainedByteSize
            ?? throw new ArgumentNullException(nameof(retainedByteSize));
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedBytes);
        _maxRetainedBytes = maxRetainedBytes;
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
        return GetOrCreate(program.Identity, context, () => create(program));
    }

    /// <summary>
    /// Finds or creates a program by its complete merged identity. This overload is also the collision-test seam:
    /// callers may construct identities with a forced bucket hash while equality still compares full source and
    /// binding signature.
    /// </summary>
    public ProgramCacheLease<TProgram> GetOrCreate(
        SkslMergedProgramIdentity identity,
        ProgramCacheContextKey context,
        Func<TProgram> create)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(create);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Entry? entry = FindEntry(identity, context);
            if (entry is not null)
            {
                _hits++;
                Touch(entry);
                if (!entry.IsLeased)
                {
                    entry.IsLeased = true;
                    try
                    {
                        _resetRuntimeBindings(entry.Program);
                    }
                    catch (Exception ex)
                    {
                        entry.IsLeased = false;
                        RemoveEntry(entry, countEviction: true);
                        RecordCleanupFailure(DisposeProgramsBestEffort([entry.Program]));
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                    }

                    return new ProgramCacheLease<TProgram>(
                        this,
                        entry,
                        entry.Program,
                        isCacheHit: true,
                        isTransient: false);
                }

                TProgram reentrant = CreateResetProgram(create, out _);
                return new ProgramCacheLease<TProgram>(
                    this,
                    entry: null,
                    reentrant,
                    isCacheHit: true,
                    isTransient: true);
            }

            _misses++;
            TProgram created = CreateResetProgram(create, out long retainedBytes);
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
                IsLeased = true,
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
        try
        {
            _resetRuntimeBindings(program);
        }
        catch (Exception ex)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(ex);
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
                if (!ReferenceEquals(entry.Program, program) || !entry.IsLeased)
                {
                    throw new InvalidOperationException(
                        "A program-cache lease does not match an active cached checkout.");
                }

                entry.IsLeased = false;
                if (primaryFailure is not null)
                {
                    if (!entry.IsEvicted)
                        RemoveEntry(entry, countEviction: true);
                    disposable.Add(program);
                }
                else if (entry.IsEvicted || _disposed)
                {
                    if (!entry.IsEvicted)
                        RemoveEntry(entry, countEviction: true);
                    disposable.Add(program);
                }
                else
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

    private TProgram CreateResetProgram(Func<TProgram> create, out long retainedBytes)
    {
        TProgram program = create()
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
        SkslMergedProgramIdentity identity,
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
        SkslMergedProgramIdentity identity,
        ProgramCacheContextKey context,
        TProgram program,
        long retainedBytes)
    {
        public SkslMergedProgramIdentity Identity { get; } = identity;

        public ProgramCacheContextKey Context { get; } = context;

        public TProgram Program { get; } = program;

        public long RetainedBytes { get; } = retainedBytes;

        public LinkedListNode<Entry>? LruNode { get; set; }

        public bool IsLeased { get; set; }

        public bool IsEvicted { get; set; }
    }
}

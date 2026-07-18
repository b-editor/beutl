using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Caches compiled runtime shader programs by a source-hash bucket plus exact ordered sources (feature 004, T035,
/// data-model §3). A fused pass looks up its program here instead of parsing SKSL every execution, so after the
/// first frame that compiles a given program <see cref="PipelineDiagnostics.ProgramCreations"/> stays at zero
/// (SC-002). Exact source comparison makes the 64-bit hash an index only: a collision creates a distinct entry.
/// The pass-owned <see cref="RuntimeProgram"/> memoizes the <see cref="SkslSnippetMerger"/> merge and holds a
/// direct entry handle: identical warm runs neither regenerate their source nor acquire the global cache-map lock.
/// </summary>
/// <remarks>
/// <para>
/// The cached unit is the <see cref="SKRuntimeShaderBuilder"/>, not the bare <see cref="SKRuntimeEffect"/>: an
/// <c>SKRuntimeShaderBuilder</c> owns its effect and disposing the builder frees the effect, so a per-frame
/// builder over a shared effect would free it on the first frame's teardown. The executor instead reuses the
/// cached builder — overwriting its per-frame uniforms/children and re-running <c>Build()</c> (whose returned
/// shader is independent and disposed per frame) — and never disposes it. The cache disposes a builder only on LRU
/// eviction.
/// </para>
/// <para>
/// <b>Scope deviation (documented):</b> data-model §3 specifies a <em>per-graphics-context</em> program cache;
/// this implementation is <b>process-wide</b>. An <see cref="SKRuntimeEffect"/> is a CPU-side SKSL parse with no
/// device handles — context-independent, surviving device loss — so sharing across contexts is sound and reuses
/// more (identical fusion groups anywhere in any scene share one program). The LRU cap and locking required by
/// T035 are preserved.
/// </para>
/// <para>
/// Cache-map mutations and cold lookups are serialized by a lock; a warm descriptor leases its resolved entry under
/// that entry's own gate. The rented builder's per-frame mutation (uniforms/children/
/// <c>Build()</c>) is not locked: the render pipeline is render-thread-affine, and a builder's SkSL-parse state is
/// immutable. Sequential reuse is NOT the only shape, though — binding a deferred child can render a
/// <c>DrawableBrush</c>, whose nested fused pass may request the SAME signature while the outer pass is mid-bind.
/// The <see cref="Lease"/> therefore marks the cached builder rented for the bind→<c>Build()</c> window; a
/// reentrant same-signature request gets a fresh transient builder (disposed with its lease) instead of resetting
/// the outer pass's bindings out from under it.
/// </para>
/// </remarks>
internal static class ProgramCache
{
    // Bounds the retained program set. Distinct effect-source combinations in a project are small (dozens); the
    // cap only guards a pathological scene that generates unbounded distinct fused programs.
    private const int Capacity = 256;

    private static readonly object s_gate = new();
    private static readonly Dictionary<string, List<LinkedListNode<Entry>>> s_map = new(StringComparer.Ordinal);
    private static readonly LinkedList<Entry> s_lru = new();
    private static int s_count;
    private static int s_coldLookupCountForTest;
    private static long s_accessClock;
    private static int s_clearing;

    internal sealed class Entry(string signature, SkslSource[] sources, SKRuntimeShaderBuilder builder)
    {
        public object Gate { get; } = new();

        public string Signature { get; } = signature;

        public SkslSource[] Sources { get; } = sources;

        public SKRuntimeShaderBuilder Builder { get; } = builder;

        public bool Rented { get; set; }

        public bool IsDisposed { get; set; }

        public long LastUsed;
    }

    /// <summary>
    /// A checked-out shader builder. Disposing returns a cache-backed builder to the cache (making it rentable
    /// again) or frees a transient one; hold the lease across the whole uniform/child bind and <c>Build()</c>.
    /// </summary>
    public readonly struct Lease(object? entry, SKRuntimeShaderBuilder builder) : IDisposable
    {
        public SKRuntimeShaderBuilder Builder => builder;

        public void Dispose()
        {
            if (entry is Entry cached)
            {
                lock (cached.Gate)
                {
                    if (!cached.IsDisposed)
                        cached.Rented = false;
                }

                if (Volatile.Read(ref s_count) > Capacity)
                {
                    lock (s_gate)
                        EvictOldestUnrented();
                }
            }
            else
            {
                builder.Dispose();
            }
        }
    }

    /// <summary>
    /// Leases the shader builder for <paramref name="program"/>, matching the ordered sources by complete text and
    /// kind. Its source and signature were generated once with the compiled pass. SKSL is parsed only on a miss — or on
    /// a reentrant request while the exact cached builder is rented, which returns a transient builder — incrementing
    /// <see cref="PipelineDiagnostics.ProgramCreations"/> either way. Callers set uniforms/children and call
    /// <c>Build()</c>, then dispose the lease (never the builder itself).
    /// </summary>
    public static Lease GetOrCreate(RuntimeProgram program, PipelineDiagnostics? diagnostics)
    {
        if (program.CacheEntry is { } resolved)
        {
            lock (resolved.Gate)
            {
                if (!resolved.IsDisposed
                    && Volatile.Read(ref s_clearing) == 0
                    && SourcesMatch(resolved.Sources, program.Sources))
                {
                    Touch(resolved);
                    if (!resolved.Rented)
                    {
                        resolved.Rented = true;
                        return new Lease(resolved, resolved.Builder);
                    }

                    return new Lease(null, CreateBuilder(program, diagnostics));
                }
            }

            program.CacheEntry = null;
        }

        Interlocked.Increment(ref s_coldLookupCountForTest);
        lock (s_gate)
        {
            if (s_map.TryGetValue(program.Signature, out List<LinkedListNode<Entry>>? bucket))
            {
                foreach (LinkedListNode<Entry> node in bucket)
                {
                    Entry foundEntry = node.Value;
                    if (!SourcesMatch(foundEntry.Sources, program.Sources))
                        continue;

                    lock (foundEntry.Gate)
                    {
                        if (foundEntry.IsDisposed)
                            continue;

                        program.CacheEntry = foundEntry;
                        Touch(foundEntry);
                        if (!foundEntry.Rented)
                        {
                            s_lru.Remove(node);
                            s_lru.AddFirst(node);
                            foundEntry.Rented = true;
                            return new Lease(foundEntry, foundEntry.Builder);
                        }

                        // Reentrant use of the same exact sources while the cached builder is mid-bind: a transient
                        // builder keeps the outer pass's bindings intact; the lease disposes it.
                        return new Lease(null, CreateBuilder(program, diagnostics));
                    }
                }
            }

            var entry = new Entry(
                program.Signature, [.. program.Sources], CreateBuilder(program, diagnostics))
            {
                Rented = true,
            };
            Touch(entry);
            var inserted = s_lru.AddFirst(entry);
            Volatile.Write(ref s_count, s_lru.Count);
            if (bucket == null)
            {
                bucket = [];
                s_map.Add(program.Signature, bucket);
            }

            bucket.Add(inserted);
            program.CacheEntry = entry;
            if (s_lru.Count > Capacity)
                EvictOldestUnrented();

            return new Lease(entry, entry.Builder);
        }
    }

    private static bool SourcesMatch(SkslSource[] cached, IReadOnlyList<SkslSource> sources)
    {
        if (cached.Length != sources.Count)
            return false;

        for (int i = 0; i < cached.Length; i++)
        {
            SkslSource current = sources[i];
            if (cached[i].Kind != current.Kind
                || !string.Equals(cached[i].Source, current.Source, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static SKRuntimeShaderBuilder CreateBuilder(RuntimeProgram program, PipelineDiagnostics? diagnostics)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.SourceText, out string? error);
        if (effect == null || error != null)
        {
            effect?.Dispose();
            throw new InvalidOperationException($"Failed to compile fused SKSL program: {error}");
        }

        if (diagnostics != null)
            diagnostics.ProgramCreations++;

        return new SKRuntimeShaderBuilder(effect);
    }

    private static void Touch(Entry entry)
        => Volatile.Write(ref entry.LastUsed, Interlocked.Increment(ref s_accessClock));

    // Disposing a rented builder would be a use-after-free in the pass holding its lease, so eviction walks past
    // rented entries; with every entry rented (pathological) the cache temporarily exceeds Capacity instead.
    private static void EvictOldestUnrented()
    {
        while (true)
        {
            LinkedListNode<Entry>? oldest = null;
            long oldestUse = long.MaxValue;
            for (LinkedListNode<Entry>? node = s_lru.First; node != null; node = node.Next)
            {
                Entry candidate = node.Value;
                lock (candidate.Gate)
                {
                    long lastUsed = Volatile.Read(ref candidate.LastUsed);
                    if (!candidate.Rented && !candidate.IsDisposed && lastUsed < oldestUse)
                    {
                        oldest = node;
                        oldestUse = lastUsed;
                    }
                }
            }

            if (oldest == null)
                return;

            Entry entry = oldest.Value;
            lock (entry.Gate)
            {
                // A warm direct lookup can rent the candidate without the global map lock. Retry the scan if it won
                // this race; disposing a rented builder would invalidate the pass currently binding it.
                if (entry.Rented || entry.IsDisposed)
                    continue;
                entry.IsDisposed = true;
            }

            s_lru.Remove(oldest);
            List<LinkedListNode<Entry>> bucket = s_map[entry.Signature];
            bucket.Remove(oldest);
            if (bucket.Count == 0)
                s_map.Remove(entry.Signature);
            entry.Builder.Dispose();
            Volatile.Write(ref s_count, s_lru.Count);
            return;
        }
    }

    /// <summary>Test-only: drops and disposes every cached program (isolates program-creation counter assertions).</summary>
    internal static void Clear()
    {
        lock (s_gate)
        {
            Volatile.Write(ref s_clearing, 1);
            try
            {
                foreach (Entry entry in s_lru)
                {
                    lock (entry.Gate)
                    {
                        if (entry.Rented)
                        {
                            throw new InvalidOperationException(
                                "ProgramCache cannot be cleared while a shader builder lease is active.");
                        }
                    }
                }

                foreach (Entry entry in s_lru)
                {
                    lock (entry.Gate)
                        entry.IsDisposed = true;
                    entry.Builder.Dispose();
                }
                s_map.Clear();
                s_lru.Clear();
                Volatile.Write(ref s_count, 0);
                Volatile.Write(ref s_coldLookupCountForTest, 0);
                Volatile.Write(ref s_accessClock, 0);
            }
            finally
            {
                Volatile.Write(ref s_clearing, 0);
            }
        }
    }

    internal static int CountForTest
    {
        get
        {
            lock (s_gate)
                return s_lru.Count;
        }
    }

    internal static int ColdLookupCountForTest => Volatile.Read(ref s_coldLookupCountForTest);
}

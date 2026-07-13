using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Caches compiled runtime shader programs by a source-identity signature (feature 004, T035, data-model §3). A
/// fused pass looks up its program here instead of parsing SKSL every execution, so after the first frame that
/// compiles a given program <see cref="PipelineDiagnostics.ProgramCreations"/> stays at zero (SC-002). The
/// signature also memoizes the <see cref="SkslSnippetMerger"/> merge: identical snippet runs never re-generate
/// their merged source.
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
/// Cache-map mutations are serialized by a lock. The rented builder's per-frame mutation (uniforms/children/
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
    private static readonly Dictionary<string, LinkedListNode<Entry>> s_map = new(StringComparer.Ordinal);
    private static readonly LinkedList<Entry> s_lru = new();

    private sealed class Entry(string signature, SKRuntimeShaderBuilder builder)
    {
        public string Signature { get; } = signature;

        public SKRuntimeShaderBuilder Builder { get; } = builder;

        public bool Rented { get; set; }
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
                lock (s_gate)
                {
                    cached.Rented = false;
                    if (s_map.Count > Capacity)
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
    /// Leases the shader builder for <paramref name="signature"/>, building its effect via
    /// <paramref name="buildSource"/> on a miss. The source is generated (and SKSL parsed) only on a miss — or on
    /// a reentrant request while the cached builder is rented, which returns a transient builder — incrementing
    /// <see cref="PipelineDiagnostics.ProgramCreations"/> either way. Callers set uniforms/children and call
    /// <c>Build()</c>, then dispose the lease (never the builder itself).
    /// </summary>
    public static Lease GetOrCreate(
        string signature, Func<string> buildSource, PipelineDiagnostics? diagnostics)
    {
        lock (s_gate)
        {
            if (s_map.TryGetValue(signature, out LinkedListNode<Entry>? node))
            {
                if (!node.Value.Rented)
                {
                    s_lru.Remove(node);
                    s_lru.AddFirst(node);
                    node.Value.Rented = true;
                    return new Lease(node.Value, node.Value.Builder);
                }

                // Reentrant same-signature use while the cached builder is mid-bind: a transient builder keeps the
                // outer pass's bindings intact; the lease disposes it.
                return new Lease(null, CreateBuilder(signature, buildSource, diagnostics));
            }

            var entry = new Entry(signature, CreateBuilder(signature, buildSource, diagnostics)) { Rented = true };
            var inserted = s_lru.AddFirst(entry);
            s_map[signature] = inserted;
            if (s_map.Count > Capacity)
                EvictOldestUnrented();

            return new Lease(entry, entry.Builder);
        }
    }

    private static SKRuntimeShaderBuilder CreateBuilder(
        string signature, Func<string> buildSource, PipelineDiagnostics? diagnostics)
    {
        string source = buildSource();
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out string? error);
        if (effect == null || error != null)
        {
            effect?.Dispose();
            throw new InvalidOperationException($"Failed to compile fused SKSL program: {error}");
        }

        if (diagnostics != null)
            diagnostics.ProgramCreations++;

        return new SKRuntimeShaderBuilder(effect);
    }

    // Disposing a rented builder would be a use-after-free in the pass holding its lease, so eviction walks past
    // rented entries; with every entry rented (pathological) the cache temporarily exceeds Capacity instead.
    private static void EvictOldestUnrented()
    {
        for (LinkedListNode<Entry>? node = s_lru.Last; node != null; node = node.Previous)
        {
            if (node.Value.Rented)
                continue;

            s_lru.Remove(node);
            s_map.Remove(node.Value.Signature);
            node.Value.Builder.Dispose();
            return;
        }
    }

    /// <summary>Test-only: drops and disposes every cached program (isolates program-creation counter assertions).</summary>
    internal static void Clear()
    {
        lock (s_gate)
        {
            foreach (LinkedListNode<Entry> node in s_map.Values)
                node.Value.Builder.Dispose();
            s_map.Clear();
            s_lru.Clear();
        }
    }

    internal static int CountForTest
    {
        get
        {
            lock (s_gate)
                return s_map.Count;
        }
    }
}

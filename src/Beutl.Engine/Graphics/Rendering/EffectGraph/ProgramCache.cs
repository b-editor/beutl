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
/// Cache-map mutations are serialized by a lock. The returned builder's per-frame mutation (uniforms/children/
/// <c>Build()</c>) is not locked: the render pipeline is render-thread-affine, and a builder's SkSL-parse state is
/// immutable, so sequential reuse on the render thread is safe.
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

    private readonly record struct Entry(string Signature, SKRuntimeShaderBuilder Builder);

    /// <summary>
    /// Returns the reusable shader builder for <paramref name="signature"/>, building its effect via
    /// <paramref name="buildSource"/> on a miss. The source is generated (and SKSL parsed) only on a miss,
    /// incrementing <see cref="PipelineDiagnostics.ProgramCreations"/>. The returned builder is owned by the cache;
    /// callers set uniforms/children and call <c>Build()</c> but must not dispose it.
    /// </summary>
    public static SKRuntimeShaderBuilder GetOrCreate(
        string signature, Func<string> buildSource, PipelineDiagnostics? diagnostics)
    {
        lock (s_gate)
        {
            if (s_map.TryGetValue(signature, out LinkedListNode<Entry>? node))
            {
                s_lru.Remove(node);
                s_lru.AddFirst(node);
                return node.Value.Builder;
            }

            string source = buildSource();
            SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out string? error);
            if (effect == null || error != null)
            {
                effect?.Dispose();
                throw new InvalidOperationException($"Failed to compile fused SKSL program: {error}");
            }

            var builder = new SKRuntimeShaderBuilder(effect);
            if (diagnostics != null)
                diagnostics.ProgramCreations++;

            var inserted = s_lru.AddFirst(new Entry(signature, builder));
            s_map[signature] = inserted;
            if (s_map.Count > Capacity)
            {
                LinkedListNode<Entry>? evicted = s_lru.Last;
                if (evicted != null)
                {
                    s_lru.RemoveLast();
                    s_map.Remove(evicted.Value.Signature);
                    evicted.Value.Builder.Dispose();
                }
            }

            return builder;
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
}

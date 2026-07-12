using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the <see cref="ProgramCache"/> lease protocol: binding a deferred child can render a DrawableBrush whose
/// nested fused pass requests the SAME program signature while the outer pass is mid-bind. Handing the nested
/// request the cached mutable <see cref="SkiaSharp.SKRuntimeShaderBuilder"/> would let it reset the outer pass's
/// uniforms/children out from under it, so a rented signature must resolve to a distinct transient builder.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ProgramCacheReentrancyTests
{
    private const string Source = "uniform shader src; half4 main(float2 p) { return src.eval(p); }";

    [Test]
    public void GetOrCreate_WhileLeased_ReturnsDistinctTransientBuilder()
    {
        ProgramCache.Clear();
        var diagnostics = new PipelineDiagnostics();

        ProgramCache.Lease outer = ProgramCache.GetOrCreate("test:reentrancy", () => Source, diagnostics);
        try
        {
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1), "the first lease parses the program once");

            ProgramCache.Lease inner = ProgramCache.GetOrCreate("test:reentrancy", () => Source, diagnostics);
            try
            {
                Assert.That(inner.Builder, Is.Not.SameAs(outer.Builder),
                    "a reentrant same-signature request must not receive the builder another pass is binding");
                Assert.That(diagnostics.ProgramCreations, Is.EqualTo(2),
                    "the transient reentrant builder is a real parse and is counted");
            }
            finally
            {
                inner.Dispose();
            }
        }
        finally
        {
            outer.Dispose();
        }

        // With every lease returned, the cached builder serves again without a new parse (SC-002 stays intact).
        ProgramCache.Lease warm = ProgramCache.GetOrCreate("test:reentrancy", () => Source, diagnostics);
        try
        {
            Assert.That(warm.Builder, Is.SameAs(outer.Builder), "the cached builder is reused once un-rented");
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(2), "a warm lease never re-parses");
        }
        finally
        {
            warm.Dispose();
            ProgramCache.Clear();
        }
    }
}

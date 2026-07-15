using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using SkiaSharp;

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
    private static readonly RuntimeProgram s_program = Program(Source, "test:reentrancy");

    [Test]
    public void GetOrCreate_WhileLeased_ReturnsDistinctTransientBuilder()
    {
        ProgramCache.Clear();
        var diagnostics = new PipelineDiagnostics();

        ProgramCache.Lease outer = ProgramCache.GetOrCreate(s_program, diagnostics);
        try
        {
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1), "the first lease parses the program once");

            ProgramCache.Lease inner = ProgramCache.GetOrCreate(s_program, diagnostics);
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
        ProgramCache.Lease warm = ProgramCache.GetOrCreate(s_program, diagnostics);
        try
        {
            Assert.That(warm.Builder, Is.SameAs(outer.Builder), "the cached builder is reused once un-rented");
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(2), "a warm lease never re-parses");
            Assert.That(ProgramCache.ColdLookupCountForTest, Is.EqualTo(1),
                "the direct descriptor handle must bypass the global cache-map lock on reentrant and warm leases");
        }
        finally
        {
            warm.Dispose();
            ProgramCache.Clear();
        }
    }

    [Test]
    public void Clear_WhileLeaseIsActive_FailsWithoutDisposingTheBuilder()
    {
        ProgramCache.Clear();
        RuntimeProgram program = Program(Source, "test:clear-active");
        ProgramCache.Lease lease = ProgramCache.GetOrCreate(program, diagnostics: null);
        SKRuntimeShaderBuilder builder = lease.Builder;
        try
        {
            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(ProgramCache.Clear);
            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Does.Contain("lease is active"));
                Assert.That(ProgramCache.CountForTest, Is.EqualTo(1),
                    "a rejected clear must preserve bookkeeping for the eventual lease return");
            });

            using SKShader child = SKShader.CreateColor(SKColors.Red);
            builder.Children["src"] = child;
            using SKShader built = builder.Build();
            Assert.That(built.Handle, Is.Not.EqualTo(IntPtr.Zero),
                "Clear must not dispose a builder that the active lease can still use");
        }
        finally
        {
            lease.Dispose();
            ProgramCache.Clear();
        }
    }

    [Test]
    public void ReturningLease_AfterAllEntriesWereRented_RestoresCapacity()
    {
        ProgramCache.Clear();
        var leases = new List<ProgramCache.Lease>();
        try
        {
            for (int i = 0; i < 257; i++)
            {
                int id = i;
                leases.Add(ProgramCache.GetOrCreate(
                    Program(Source, $"test:capacity:{id}"), diagnostics: null));
            }

            Assert.That(ProgramCache.CountForTest, Is.EqualTo(257),
                "all entries are rented, so the cache may temporarily exceed its 256-entry cap");

            leases[0].Dispose();
            leases.RemoveAt(0);
            Assert.That(ProgramCache.CountForTest, Is.EqualTo(256),
                "returning the first rentable entry must immediately restore the retained cache cap");
        }
        finally
        {
            foreach (ProgramCache.Lease lease in leases)
                lease.Dispose();
            ProgramCache.Clear();
        }
    }

    [Test]
    public void HashBucketCollision_UsesCompleteSourceIdentity()
    {
        const string otherSource =
            "uniform shader src; half4 main(float2 p) { return half4(1, 0, 0, 1); }";
        RuntimeProgram firstProgram = Program(Source, "test:forced-hash-collision");
        RuntimeProgram otherProgram = Program(otherSource, "test:forced-hash-collision");
        ProgramCache.Clear();
        var diagnostics = new PipelineDiagnostics();

        SKRuntimeShaderBuilder? firstBuilder;
        using (ProgramCache.Lease first = ProgramCache.GetOrCreate(firstProgram, diagnostics))
        {
            firstBuilder = first.Builder;
        }

        using (ProgramCache.Lease second = ProgramCache.GetOrCreate(otherProgram, diagnostics))
        {
            Assert.That(second.Builder, Is.Not.SameAs(firstBuilder),
                "different sources in one hash bucket must compile to distinct programs");
        }

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(2));
            Assert.That(ProgramCache.CountForTest, Is.EqualTo(2));
        });
        ProgramCache.Clear();
    }

    private static RuntimeProgram Program(string source, string signature)
    {
        SkslSource sksl = SkslSource.WholeSource(source);
        return new RuntimeProgram(
            startStage: 0,
            stageCount: 1,
            isWholeSource: true,
            signature,
            sources: [sksl],
            sourceText: source);
    }
}

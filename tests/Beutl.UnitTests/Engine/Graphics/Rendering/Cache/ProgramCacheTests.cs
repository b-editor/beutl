using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class ProgramCacheTests
{
    private const string SourceA = "half4 main(float2 p) { return half4(1); }";
    private const string SourceB = "half4 main(float2 p) { return half4(0); }";

    [Test]
    public void SpirvExecution_RejectsAnUnknownThreeDimensionalContext()
    {
        var context = new Mock<IGraphicsContext>();
        context.SetupGet(x => x.Supports3DRendering).Returns(true);

        Assert.That(SpirvShaderProgramCache.SupportsExecution(context.Object), Is.False);
    }

    [Test]
    public void GetOrCreate_MergedProgramFactory_ReceivesColdProgramOnly()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        SkslMergedProgram first = SkslSnippetMerger.Merge([new SkslSnippetStage(description)]);
        SkslMergedProgram equivalent = SkslSnippetMerger.Merge([new SkslSnippetStage(description)]);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        SkslMergedProgram? factoryArgument = null;
        int factoryCalls = 0;

        FakeProgram created;
        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(first, context, Create))
        {
            created = lease.Program;
        }

        using (ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(equivalent, context, Create))
        {
            Assert.Multiple(() =>
            {
                Assert.That(warmed.Program, Is.SameAs(created));
                Assert.That(warmed.IsCacheHit, Is.True);
                Assert.That(factoryCalls, Is.EqualTo(1));
                Assert.That(factoryArgument, Is.SameAs(first));
            });
        }

        FakeProgram Create(SkslMergedProgram program)
        {
            factoryCalls++;
            factoryArgument = program;
            return new FakeProgram(factoryCalls, 16);
        }
    }

    [Test]
    public void GetOrCreate_WarmedEquivalentIdentity_ReusesOneImmutableProgram()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity firstIdentity = Identity(SourceA);
        ShaderProgramIdentity equivalentIdentity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;

        FakeProgram firstProgram;
        using (ProgramCacheLease<FakeProgram> first = cache.GetOrCreate(
                   firstIdentity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
            firstProgram = first.Program;
            Assert.That(first.IsCacheHit, Is.False);
        }

        using (ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(
                   equivalentIdentity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
            Assert.Multiple(() =>
            {
                Assert.That(warmed.IsCacheHit, Is.True);
                Assert.That(warmed.Program, Is.SameAs(firstProgram));
            });
        }

        ProgramCacheStatistics statistics = cache.Statistics;
        Assert.Multiple(() =>
        {
            Assert.That(statistics.Hits, Is.EqualTo(1));
            Assert.That(statistics.Misses, Is.EqualTo(1));
            Assert.That(statistics.Creations, Is.EqualTo(1));
            Assert.That(statistics.RetainedPrograms, Is.EqualTo(1));
            Assert.That(statistics.RetainedBytes, Is.EqualTo(16));
        });
    }

    [Test]
    [NonParallelizable]
    public void GetOrCreate_WarmedHitAllocatesOnlyItsLease()
    {
        const int iterations = 1_000;
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        using (cache.GetOrCreate(identity, context, () => new FakeProgram(1, 16)))
        {
        }

        for (int index = 0; index < 100; index++)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                static () => throw new AssertionException("A warmed hit must not invoke its factory."));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                static () => throw new AssertionException("A warmed hit must not invoke its factory."));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long perLookup = allocated / iterations;
        TestContext.Out.WriteLine($"warmed program-cache hit: {perLookup} bytes/lookup");

        Assert.That(
            perLookup,
            Is.LessThanOrEqualTo(64),
            "a warmed hit should allocate its lease, not empty cleanup collections");
    }

    [Test]
    [NonParallelizable]
    public void GetOrCreate_DistinctColdEntriesAvoidPerEntryBucketCollections()
    {
        const int count = 512;
        using var cache = CreateCache(maxRetainedBytes: count);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        ShaderProgramIdentity[] identities = Enumerable.Range(0, count)
            .Select(index => Identity(SourceA + index))
            .ToArray();
        FakeProgram[] programs = Enumerable.Range(0, count)
            .Select(index => new FakeProgram(index, 1))
            .ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identities[index],
                context,
                programs[index],
                static program => program);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long perEntry = allocated / count;
        TestContext.Out.WriteLine($"distinct cold program-cache entries: {perEntry} bytes/entry");

        Assert.That(
            perEntry,
            Is.LessThanOrEqualTo(304),
            "the cache should rely on Dictionary collision handling instead of allocating a list per hash");
    }

    [Test]
    public void GetOrCreate_SameSourceForDifferentBackends_DoesNotCollide()
    {
        const string spirvSource =
            "#version 450\nlayout(location=0) out vec4 color; void main() { color = vec4(1); }";
        var lowering = new SpirvShaderLowering(
            spirvSource,
            []);
        ShaderProgramIdentity skslIdentity = ShaderProgramIdentity.CreateSksl(
            spirvSource,
            [],
            SkslBackendBudgetResolver.SpirvVulkan);
        ShaderProgramIdentity spirvIdentity = lowering.ProgramIdentity;
        using var cache = CreateCache(maxRetainedBytes: 64);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;

        FakeProgram skslProgram;
        using (ProgramCacheLease<FakeProgram> sksl = cache.GetOrCreate(
                   skslIdentity,
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            skslProgram = sksl.Program;
        }

        using ProgramCacheLease<FakeProgram> spirv = cache.GetOrCreate(
            spirvIdentity,
            context,
            () => new FakeProgram(++nextId, 8));

        Assert.Multiple(() =>
        {
            Assert.That(skslIdentity, Is.Not.EqualTo(spirvIdentity));
            Assert.That(spirv.Program, Is.Not.SameAs(skslProgram));
            Assert.That(spirv.IsCacheHit, Is.False);
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(2));
        });
    }

    [Test]
    public void ShaderIdentity_DifferentBackendBudgetsAreNotEqual()
    {
        ShaderProgramIdentity unlimited = ShaderProgramIdentity.CreateSksl(
            SourceA,
            [],
            SkslBackendBudget.Unlimited);
        ShaderProgramIdentity limited = ShaderProgramIdentity.CreateSksl(
            SourceA,
            [],
            new SkslBackendBudget(
                "limited",
                maxStages: 1,
                maxUniformVectors: 1,
                maxSamplers: 1,
                maxChildren: 1,
                maxSourceBytes: 1,
                maxProgramTokens: 1));

        Assert.That(limited, Is.Not.EqualTo(unlimited));
    }

    [Test]
    public void GetOrCreate_HashBucketCollision_UsesFullSourceAndBindingSignature()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        const string collisionSourceA = SourceA + " // bHclWWfdRgO1";
        const string collisionSourceB = SourceA + " // cQCqecdMIEfo";
        ShaderProgramIdentity sourceA = Identity(collisionSourceA);
        ShaderProgramIdentity sourceB = Identity(collisionSourceB);
        ShaderProgramIdentity differentSignature = Identity(
            collisionSourceA,
            [new SkslMergedBindingLayout(
                0,
                0,
                SkslBindingKind.Uniform,
                "gain",
                "__beutl_s0_gain",
                "float",
                null,
                null)]);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;

        FakeProgram first;
        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                   sourceA,
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            first = lease.Program;
        }

        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                   sourceB,
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            Assert.That(lease.Program, Is.Not.SameAs(first));
        }

        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                   differentSignature,
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            Assert.That(lease.Program, Is.Not.SameAs(first));
        }

        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                   Identity(collisionSourceA),
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            Assert.That(lease.Program, Is.SameAs(first));
        }

        Assert.Multiple(() =>
        {
            Assert.That(sourceA.GetHashCode(), Is.EqualTo(sourceB.GetHashCode()));
            Assert.That(sourceA, Is.Not.EqualTo(sourceB));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(3));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(3));
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(3));
        });
    }

    [Test]
    public void GetOrCreate_ConcurrentLeasesReuseOneProgramUntilTheLastLeaseReturns()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int creations = 0;

        ProgramCacheLease<FakeProgram> outer = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++creations, 16));
        ProgramCacheLease<FakeProgram> inner = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++creations, 16));
        FakeProgram program = outer.Program;

        Assert.Multiple(() =>
        {
            Assert.That(inner.Program, Is.SameAs(program));
            Assert.That(inner.IsCacheHit, Is.True);
            Assert.That(creations, Is.EqualTo(1));
        });

        Assert.That(cache.EvictContext("device-a", "context-a"), Is.EqualTo(1));
        outer.Dispose();
        Assert.That(program.DisposeCount, Is.Zero);
        inner.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(program.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });
    }

    [Test]
    public void SynchronizeContext_EvictsProgramsFromThePreviousDestinationContext()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey firstContext = Context("device-a", "context-a");
        FakeProgram program;
        using (ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                   identity,
                   firstContext,
                   () => new FakeProgram(1, 16)))
        {
            program = lease.Program;
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.SynchronizeContext("device-a", "context-a"), Is.Zero);
            Assert.That(cache.SynchronizeContext("device-a", "context-b"), Is.EqualTo(1));
            Assert.That(program.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });
    }

    [Test]
    public void GetOrCreate_ContextCompileContract_IsPartOfTheFullKey()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey[] contexts =
        [
            Context("device-a", "context-a", capability: "skia-v1", format: "rgba16f", options: "default"),
            Context("device-b", "context-a", capability: "skia-v1", format: "rgba16f", options: "default"),
            Context("device-a", "context-b", capability: "skia-v1", format: "rgba16f", options: "default"),
            Context("device-a", "context-a", capability: "skia-v2", format: "rgba16f", options: "default"),
            Context("device-a", "context-a", capability: "skia-v1", format: "rgba8", options: "default"),
            Context("device-a", "context-a", capability: "skia-v1", format: "rgba16f", options: "optimized"),
        ];
        int nextId = 0;

        foreach (ProgramCacheContextKey context in contexts)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                () => new FakeProgram(++nextId, 8));
            Assert.That(lease.IsCacheHit, Is.False);
        }

        using ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(
            identity,
            Context("device-a", "context-a", capability: "skia-v1", format: "rgba16f", options: "default"),
            () => new FakeProgram(++nextId, 8));
        Assert.Multiple(() =>
        {
            Assert.That(warmed.IsCacheHit, Is.True);
            Assert.That(cache.Statistics.Misses, Is.EqualTo(contexts.Length));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(contexts.Length));
        });
    }

    [Test]
    public void ByteBudget_EvictsLeastRecentlyUsedAvailableProgram()
    {
        using var cache = CreateCache(maxRetainedBytes: 20);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        ShaderProgramIdentity a = Identity(SourceA + "// a");
        ShaderProgramIdentity b = Identity(SourceA + "// b");
        ShaderProgramIdentity c = Identity(SourceA + "// c");
        int nextId = 0;

        FakeProgram programA = AcquireAndReturn(a);
        FakeProgram programB = AcquireAndReturn(b);
        Assert.That(AcquireAndReturn(a), Is.SameAs(programA), "A is now the most recently used entry");
        _ = AcquireAndReturn(c);

        Assert.Multiple(() =>
        {
            Assert.That(programA.DisposeCount, Is.Zero);
            Assert.That(programB.DisposeCount, Is.EqualTo(1), "B is the least recently used available entry");
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(2));
            Assert.That(cache.Statistics.RetainedBytes, Is.EqualTo(20));
            Assert.That(cache.Statistics.Evictions, Is.EqualTo(1));
        });

        using ProgramCacheLease<FakeProgram> recreatedB = cache.GetOrCreate(
            b,
            context,
            () => new FakeProgram(++nextId, 10));
        Assert.Multiple(() =>
        {
            Assert.That(recreatedB.IsCacheHit, Is.False);
            Assert.That(recreatedB.Program, Is.Not.SameAs(programB));
        });

        FakeProgram AcquireAndReturn(ShaderProgramIdentity identity)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                () => new FakeProgram(++nextId, 10));
            return lease.Program;
        }
    }

    [Test]
    public void ByteBudget_TrimsAnAvailableProgramAfterAConcurrentLeaseReturns()
    {
        using var cache = CreateCache(maxRetainedBytes: 16);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        ProgramCacheLease<FakeProgram> first = cache.GetOrCreate(
            Identity(SourceA),
            context,
            () => new FakeProgram(1, 16));
        ProgramCacheLease<FakeProgram> second = cache.GetOrCreate(
            Identity(SourceB),
            context,
            () => new FakeProgram(2, 16));
        FakeProgram firstProgram = first.Program;
        FakeProgram secondProgram = second.Program;

        Assert.That(cache.Statistics.RetainedBytes, Is.EqualTo(32));
        second.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(firstProgram.DisposeCount, Is.Zero);
            Assert.That(secondProgram.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedBytes, Is.EqualTo(16));
        });
        first.Dispose();
    }

    [Test]
    public void ByteBudget_EvictionDefersItsCleanupFailureUntilCacheDisposal()
    {
        var failure = new InvalidOperationException("evicted-program-dispose");
        var cache = CreateCache(maxRetainedBytes: 8);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        var evicted = new FakeProgram(1, 8, failure);
        var retained = new FakeProgram(2, 8);
        using (cache.GetOrCreate(Identity(SourceA), context, () => evicted))
        {
        }

        Assert.DoesNotThrow(() =>
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                Identity(SourceB),
                context,
                () => retained);
        });

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(cache.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(evicted.DisposeCount, Is.EqualTo(1));
            Assert.That(retained.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
            Assert.DoesNotThrow(cache.Dispose);
        });
    }

    [Test]
    public void OversizedProgram_IsNotRetainedAndNeverBecomesAWarmedHit()
    {
        using var cache = CreateCache(maxRetainedBytes: 8);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        var programs = new List<FakeProgram>();

        for (int i = 0; i < 2; i++)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(identity, context, Create);
            Assert.That(lease.IsCacheHit, Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(programs, Has.All.Matches<FakeProgram>(static program => program.DisposeCount == 1));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(2));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
            Assert.That(cache.Statistics.RetainedBytes, Is.Zero);
        });

        FakeProgram Create()
        {
            var program = new FakeProgram(programs.Count + 1, 9);
            programs.Add(program);
            return program;
        }
    }

    [Test]
    public void EvictContext_RemovesOnlyMatchingEntries()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey a1 = Context("device-a", "context-1");
        ProgramCacheContextKey a2 = Context("device-a", "context-2");
        ProgramCacheContextKey b1 = Context("device-b", "context-1");
        int nextId = 0;

        FakeProgram programA1 = AcquireAndReturn(a1);
        FakeProgram programA2 = AcquireAndReturn(a2);
        FakeProgram programB1 = AcquireAndReturn(b1);

        Assert.That(cache.EvictContext("device-a", "context-1"), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(programA1.DisposeCount, Is.EqualTo(1));
            Assert.That(programA2.DisposeCount, Is.Zero);
            Assert.That(programB1.DisposeCount, Is.Zero);
        });

        Assert.That(cache.EvictContext("device-a", "context-2"), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(programA2.DisposeCount, Is.EqualTo(1));
            Assert.That(programB1.DisposeCount, Is.Zero);
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(1));
        });

        using ProgramCacheLease<FakeProgram> warmB = cache.GetOrCreate(
            identity,
            b1,
            () => new FakeProgram(++nextId, 8));
        Assert.That(warmB.Program, Is.SameAs(programB1));

        FakeProgram AcquireAndReturn(ProgramCacheContextKey context)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                () => new FakeProgram(++nextId, 8));
            return lease.Program;
        }
    }

    [Test]
    public void EvictContext_WhileLeased_DefersDisposalAndMakesLaterLookupMiss()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;
        ProgramCacheLease<FakeProgram> outer = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++nextId, 16));
        FakeProgram invalidated = outer.Program;

        Assert.That(cache.EvictContext("device-a", "context-a"), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(invalidated.DisposeCount, Is.Zero,
                "context eviction cannot dispose an immutable program while its lease is still executing");
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });

        FakeProgram replacementProgram;
        using (ProgramCacheLease<FakeProgram> replacement = cache.GetOrCreate(
                   identity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
            replacementProgram = replacement.Program;
            Assert.Multiple(() =>
            {
                Assert.That(replacement.IsCacheHit, Is.False);
                Assert.That(replacement.Program, Is.Not.SameAs(invalidated));
            });
        }

        outer.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(invalidated.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(2));
        });
        using ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++nextId, 16));
        Assert.That(warmed.Program, Is.SameAs(replacementProgram));
    }

    [Test]
    public void RetainedSizeFailure_DisposesTheCreatedProgramAndPreservesTheFailure()
    {
        var failure = new InvalidOperationException("retained-size");
        using var cache = new ProgramCache<FakeProgram>(
            _ => throw failure,
            maxRetainedBytes: 64);
        var program = new FakeProgram(1, 16);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrCreate(
                Identity(SourceA),
                Context("device-a", "context-a"),
                () => program));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(program.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });
    }

    [Test]
    public void Dispose_WithActiveLease_DefersItsProgramAndRejectsLaterLookup()
    {
        var cache = CreateCache(maxRetainedBytes: 64);
        ShaderProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(1, 16));
        FakeProgram program = lease.Program;

        cache.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(program.DisposeCount, Is.Zero);
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => cache.GetOrCreate(
                identity,
                context,
                () => new FakeProgram(2, 16)));
        });

        lease.Dispose();
        cache.Dispose();
        Assert.That(program.DisposeCount, Is.EqualTo(1));
    }

    private static ProgramCache<FakeProgram> CreateCache(long maxRetainedBytes)
        => new(
            static program => program.RetainedBytes,
            maxRetainedBytes);

    private static ProgramCacheContextKey Context(
        object device,
        object context,
        object? capability = null,
        string format = "linear-premul-rgba16f",
        object? options = null)
        => new(
            device,
            context,
            capability ?? "skia-default",
            format,
            options ?? "default");

    private static ShaderProgramIdentity Identity(
        string source,
        ImmutableArray<SkslMergedBindingLayout> bindings = default)
        => ShaderProgramIdentity.CreateSksl(
            source,
            bindings.IsDefault ? [] : bindings,
            SkslBackendBudget.Unlimited);

    private sealed class FakeProgram(
        int id,
        long retainedBytes,
        Exception? disposeFailure = null) : IDisposable
    {
        public int Id { get; } = id;

        public long RetainedBytes { get; } = retainedBytes;

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (disposeFailure is not null)
                throw disposeFailure;
        }

        public override string ToString() => $"FakeProgram {Id}";
    }
}

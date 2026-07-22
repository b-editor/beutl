using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class ProgramCacheTests
{
    private const string SourceA = "half4 main(float2 p) { return half4(1); }";
    private const string SourceB = "half4 main(float2 p) { return half4(0); }";

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
    public void GetOrCreate_WarmedEquivalentIdentity_IsAHitWithoutAnotherCreation()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        SkslMergedProgramIdentity firstIdentity = Identity(SourceA);
        SkslMergedProgramIdentity equivalentIdentity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;

        FakeProgram firstProgram;
        using (ProgramCacheLease<FakeProgram> first = cache.GetOrCreate(
                   firstIdentity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
            firstProgram = first.Program;
            first.Program.Bindings["gain"] = 7;
            Assert.Multiple(() =>
            {
                Assert.That(first.IsCacheHit, Is.False);
                Assert.That(first.IsTransient, Is.False);
                Assert.That(first.Program.ResetCount, Is.EqualTo(1));
            });
        }

        using (ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(
                   equivalentIdentity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
            Assert.Multiple(() =>
            {
                Assert.That(warmed.IsCacheHit, Is.True);
                Assert.That(warmed.IsTransient, Is.False);
                Assert.That(warmed.Program, Is.SameAs(firstProgram));
                Assert.That(warmed.Program.Bindings, Is.Empty,
                    "runtime bindings from the preceding lease must never survive a warmed hit");
                Assert.That(warmed.Program.ResetCount, Is.EqualTo(3),
                    "a cached program is reset both when returned and immediately before it is leased again");
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
    public void GetOrCreate_HashBucketCollision_UsesFullSourceAndBindingSignature()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        const int forcedBucket = 12345;
        SkslMergedProgramIdentity sourceA = Identity(SourceA, forcedBucket);
        SkslMergedProgramIdentity sourceB = Identity(SourceB, forcedBucket);
        SkslMergedProgramIdentity differentSignature = Identity(
            SourceA,
            forcedBucket,
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
                   Identity(SourceA, forcedBucket),
                   context,
                   () => new FakeProgram(++nextId, 8)))
        {
            Assert.That(lease.Program, Is.SameAs(first));
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(3));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(3));
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(3));
        });
    }

    [Test]
    public void GetOrCreate_ReentrantExactKey_UsesResetTransientWithoutCorruptingOuterBindings()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        SkslMergedProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        var created = new List<FakeProgram>();

        FakeProgram outerProgram;
        using (ProgramCacheLease<FakeProgram> outer = cache.GetOrCreate(identity, context, Create))
        {
            outerProgram = outer.Program;
            outer.Program.Bindings["outer"] = 41;
            using (ProgramCacheLease<FakeProgram> inner = cache.GetOrCreate(identity, context, Create))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(inner.IsCacheHit, Is.True,
                        "the exact cached identity was found even though its mutable instance was already leased");
                    Assert.That(inner.IsTransient, Is.True);
                    Assert.That(inner.Program, Is.Not.SameAs(outer.Program));
                    Assert.That(inner.Program.Bindings, Is.Empty);
                });

                inner.Program.Bindings["inner"] = 99;
                Assert.That(outer.Program.Bindings["outer"], Is.EqualTo(41));
            }

            Assert.Multiple(() =>
            {
                Assert.That(created[1].Bindings, Is.Empty);
                Assert.That(created[1].DisposeCount, Is.EqualTo(1));
                Assert.That(outer.Program.Bindings["outer"], Is.EqualTo(41));
            });
        }

        using (ProgramCacheLease<FakeProgram> warmed = cache.GetOrCreate(identity, context, Create))
        {
            Assert.Multiple(() =>
            {
                Assert.That(warmed.IsCacheHit, Is.True);
                Assert.That(warmed.IsTransient, Is.False);
                Assert.That(warmed.Program, Is.SameAs(outerProgram));
                Assert.That(warmed.Program.Bindings, Is.Empty);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Hits, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(1));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(2));
        });

        FakeProgram Create()
        {
            var program = new FakeProgram(created.Count + 1, 16);
            created.Add(program);
            return program;
        }
    }

    [Test]
    public void GetOrCreate_ContextCompileContract_IsPartOfTheFullKey()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        SkslMergedProgramIdentity identity = Identity(SourceA);
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
        SkslMergedProgramIdentity a = Identity(SourceA + "// a");
        SkslMergedProgramIdentity b = Identity(SourceA + "// b");
        SkslMergedProgramIdentity c = Identity(SourceA + "// c");
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

        FakeProgram AcquireAndReturn(SkslMergedProgramIdentity identity)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
                identity,
                context,
                () => new FakeProgram(++nextId, 10));
            return lease.Program;
        }
    }

    [Test]
    public void OversizedProgram_IsTransientAndNeverBecomesAWarmedHit()
    {
        using var cache = CreateCache(maxRetainedBytes: 8);
        SkslMergedProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        var programs = new List<FakeProgram>();

        for (int i = 0; i < 2; i++)
        {
            using ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(identity, context, Create);
            Assert.Multiple(() =>
            {
                Assert.That(lease.IsCacheHit, Is.False);
                Assert.That(lease.IsTransient, Is.True);
            });
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
    public void EvictContextAndDevice_RemoveOnlyMatchingEntries()
    {
        using var cache = CreateCache(maxRetainedBytes: 128);
        SkslMergedProgramIdentity identity = Identity(SourceA);
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

        Assert.That(cache.EvictDevice("device-a"), Is.EqualTo(1));
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
    public void EvictDevice_WhileLeased_DefersDisposalAndMakesLaterLookupMiss()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        SkslMergedProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;
        ProgramCacheLease<FakeProgram> outer = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++nextId, 16));
        FakeProgram invalidated = outer.Program;

        Assert.That(cache.EvictDevice("device-a"), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(invalidated.DisposeCount, Is.Zero,
                "device loss cannot invalidate a mutable program while its outer lease is still executing");
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });

        using (ProgramCacheLease<FakeProgram> replacement = cache.GetOrCreate(
                   identity,
                   context,
                   () => new FakeProgram(++nextId, 16)))
        {
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
    }

    [Test]
    public void RuntimeResetFailure_EvictsAndDisposesPoisonedProgram()
    {
        using var cache = CreateCache(maxRetainedBytes: 64);
        SkslMergedProgramIdentity identity = Identity(SourceA);
        ProgramCacheContextKey context = Context("device-a", "context-a");
        int nextId = 0;
        ProgramCacheLease<FakeProgram> lease = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++nextId, 16));
        FakeProgram poisoned = lease.Program;
        poisoned.ThrowOnNextReset = true;

        Assert.Throws<InvalidOperationException>(lease.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(poisoned.DisposeCount, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
        });

        using ProgramCacheLease<FakeProgram> replacement = cache.GetOrCreate(
            identity,
            context,
            () => new FakeProgram(++nextId, 16));
        Assert.That(replacement.IsCacheHit, Is.False);
    }

    [Test]
    public void Dispose_WithActiveLease_DefersItsProgramAndRejectsLaterLookup()
    {
        var cache = CreateCache(maxRetainedBytes: 64);
        SkslMergedProgramIdentity identity = Identity(SourceA);
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
            static program => program.ResetRuntimeBindings(),
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

    private static SkslMergedProgramIdentity Identity(
        string source,
        int? bucketHashOverride = null,
        IReadOnlyList<SkslMergedBindingLayout>? bindings = null)
        => new(
            source,
            bindings ?? [],
            SkslBackendBudget.Unlimited,
            bucketHashOverride);

    private sealed class FakeProgram(int id, long retainedBytes) : IDisposable
    {
        public int Id { get; } = id;

        public long RetainedBytes { get; } = retainedBytes;

        public Dictionary<string, int> Bindings { get; } = [];

        public int ResetCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool ThrowOnNextReset { get; set; }

        public void ResetRuntimeBindings()
        {
            ResetCount++;
            Bindings.Clear();
            if (ThrowOnNextReset)
            {
                ThrowOnNextReset = false;
                throw new InvalidOperationException("Injected runtime reset failure.");
            }
        }

        public void Dispose()
        {
            DisposeCount++;
        }

        public override string ToString() => $"FakeProgram {Id}";
    }
}

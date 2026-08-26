using System.Buffers;
using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins the buffer lifetimes the recording path depends on, and the per-visit allocation they buy.
/// </summary>
/// <remarks>
/// A recorded fragment outlives the request that made it - the recording cache replays it on later frames -
/// so only a buffer nothing reads after its recording sealed may come from a pool. These tests hold that
/// line from both sides: the pooled scratch is returned exactly once even when a replay throws, and nothing
/// a replay leaves behind aliases it.
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class RecordingBufferPoolingTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);
    private static readonly PixelSize s_frameSize = new(240, 160);

    [TearDown]
    public void TearDown()
    {
        NodeRecordingTransaction.ReplayScratchPool = ArrayPool<RenderFragmentReference>.Shared;
    }

    [Test]
    public void AReplayedFragmentsInputs_DoNotAliasTheReplayScratch()
    {
        // One buffer for every rent, poisoned on return: a replay that handed its scratch to a fragment as
        // that fragment's Inputs would see the next replay overwrite it.
        var pool = new SingleBufferPool();
        NodeRecordingTransaction.ReplayScratchPool = pool;

        using var node = new ChainedSourceNode(s_bounds);
        Record(node);
        RecordedRenderGraph replayed = Record(node);
        IReadOnlyList<RenderFragmentReference> before = ReadFragmentInputs(replayed);

        Record(node);
        Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(pool.Rents, Is.GreaterThan(1), "the replay path has to have taken the scratch");
            Assert.That(
                ReadFragmentInputs(replayed),
                Is.EqualTo(before),
                "a replayed fragment's inputs must survive the next replay reusing the same buffer");
            foreach (RenderFragmentReference reference in before)
                Assert.That(reference, Is.Not.Null, "no input slot may be left poisoned");
        });
    }

    [Test]
    public void ANestedRecordingDrivenFromItsParent_DoesNotDisturbTheParentsFragments()
    {
        var pool = new SingleBufferPool();
        NodeRecordingTransaction.ReplayScratchPool = pool;

        using var inner = new ChainedSourceNode(s_bounds);
        using var outer = new DrivingNode(inner, s_bounds);

        RecordedRenderGraph first = Record(outer);
        RecordedRenderGraph second = Record(outer);
        RecordedRenderGraph third = Record(outer);

        Assert.Multiple(() =>
        {
            Assert.That(
                second.Fragments,
                Has.Length.EqualTo(first.Fragments.Length),
                "driving a node from inside Process must record the same shape every request");
            Assert.That(third.Fragments, Has.Length.EqualTo(first.Fragments.Length));

            // Every fragment input has to name a fragment committed earlier in the same graph. A scratch
            // shared between the two live transactions would resolve one of them to the other's fragment.
            AssertInputsResolveWithinTheirOwnGraph(first);
            AssertInputsResolveWithinTheirOwnGraph(second);
            AssertInputsResolveWithinTheirOwnGraph(third);
        });
    }

    [Test]
    public void AReplayThatThrowsMidWay_ReturnsItsScratchExactlyOnce()
    {
        var pool = new CountingPool();
        NodeRecordingTransaction.ReplayScratchPool = pool;

        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(CreateOptions(owner));
        var transaction = new NodeRecordingTransaction(
            new RenderRequestRecorder(request),
            new object(),
            []);

        // The second fragment declares an input the replay is not given, so ResolveSlot throws with the
        // scratch rented and two fragments already written into it.
        RenderNodeRecordingSnapshot snapshot = CreateSnapshot(inputSlotOfSecondFragment: -4);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => transaction.ReplayRecording(snapshot, []));

        Assert.Multiple(() =>
        {
            Assert.That(pool.Rents, Is.EqualTo(1), "the replay takes exactly one scratch buffer");
            Assert.That(pool.Returns, Is.EqualTo(1), "a throw mid-replay must still return it");
            Assert.That(pool.Outstanding, Is.Empty, "no rented buffer may be left with the pool");
            Assert.That(pool.DoubleReturns, Is.Empty, "a buffer returned twice would be handed to two renters");
            Assert.That(
                pool.ReturnedCleared,
                Is.True,
                "a returned buffer still holding fragments would pin the failed request's graph");
        });
    }

    [Test]
    public void ASucceedingReplay_ReturnsItsScratchExactlyOnce()
    {
        var pool = new CountingPool();
        NodeRecordingTransaction.ReplayScratchPool = pool;

        using var node = new ChainedSourceNode(s_bounds);
        Record(node);
        Record(node);
        Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(pool.Rents, Is.GreaterThan(0));
            Assert.That(pool.Returns, Is.EqualTo(pool.Rents));
            Assert.That(pool.Outstanding, Is.Empty);
            Assert.That(pool.DoubleReturns, Is.Empty);
        });
    }

    [Test]
    public void TheCrossCheckStillAgrees_OverARepeatedlyReplayedSubtree()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        using var leaf = new ChainedSourceNode(s_bounds);
        using var middle = new PassThroughContainerNode();
        using var root = new PassThroughContainerNode();
        middle.AddChild(leaf);
        root.AddChild(middle);

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(
                () =>
                {
                    for (int frame = 0; frame < 8; frame++)
                        Record(root);
                },
                Throws.Nothing,
                "a pooled buffer that corrupted a retained recording would surface here");
        }
    }

    [Test]
    public void TheCrossCheckStillAgrees_OverTheRepresentativeScene()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        Assert.That(
            () => RenderThread.Dispatcher.Invoke(static () =>
            {
                using (RenderRecordingCrossCheck.Enable())
                    RenderRepresentativeScene(frames: 6);
            }),
            Throws.Nothing,
            "the cross-check re-records every node and compares - a pooled buffer that corrupted a retained "
            + "recording would surface as a disagreement here");
    }

    /// <summary>Renders the scene the allocation budgets are measured on, with the render cache warm.</summary>
    private static void RenderRepresentativeScene(int frames)
    {
        Drawable.Resource[] resources = CreateSceneResources();
        try
        {
            using var root = new DrawableRenderNode(resources[0]);
            using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
            {
                context.Clear();
                foreach (Drawable.Resource resource in resources)
                    context.DrawDrawable(resource);
            }

            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Preview,
                        TargetDomain = new Rect(default, s_frameSize.ToSize(1)),
                        CacheOptions = RenderCacheOptions.Enabled,
                        Purpose = RenderRequestPurpose.Frame,
                    },
                    TargetFactory = new CpuTargetFactory(),
                });

            var revalidated = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
            for (int frame = 0; frame < frames; frame++)
            {
                ClearChanges(root, revalidated);
                renderer.Rasterize().Dispose();
            }
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }
    }

    private static void ClearChanges(RenderNode root, HashSet<RenderNode> revalidated)
    {
        revalidated.Clear();
        Visit(root);
        return;

        void Visit(RenderNode current)
        {
            if (current.IsDisposed || !revalidated.Add(current))
                return;

            ReadOnlySpan<RenderNode> children = current.ChildNodes;
            for (int index = 0; index < children.Length; index++)
                Visit(children[index]);

            current.ClearChanges(current.ChangeVersion);
        }
    }

    private static Drawable.Resource[] CreateSceneResources()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = s_frameSize.Width },
            Height = { CurrentValue = s_frameSize.Height },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return
        [
            background.ToResource(context),
            accent.ToResource(context),
            label.ToResource(context),
        ];
    }

    private static void AssertInputsResolveWithinTheirOwnGraph(RecordedRenderGraph graph)
    {
        var committed = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RecordedRenderFragment fragment in graph.Fragments)
        {
            var reference = (RenderFragmentReference)fragment.Payload!;
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                Assert.That(
                    committed,
                    Does.Contain(input),
                    "a fragment input must name a fragment committed earlier in the same graph");
            }

            committed.Add(reference);
        }
    }

    private static IReadOnlyList<RenderFragmentReference> ReadFragmentInputs(RecordedRenderGraph graph)
    {
        var inputs = new List<RenderFragmentReference>();
        foreach (RecordedRenderFragment fragment in graph.Fragments)
            inputs.AddRange(((RenderFragmentReference)fragment.Payload!).Inputs);
        return inputs;
    }

    private static RenderNodeRecordingSnapshot CreateSnapshot(int inputSlotOfSecondFragment)
    {
        RenderFragmentReference first = CreateTemplate();
        RenderFragmentReference second = CreateTemplate();
        return new RenderNodeRecordingSnapshot(
            RenderNodeRecordingKey.Create(CreateOptions(null), transactionCacheEnabled: false),
            [],
            [
                new ReplayedRenderFragment(first, new object(), RenderNodeRecordingCache.ProcessRole, []),
                new ReplayedRenderFragment(
                    second,
                    new object(),
                    RenderNodeRecordingCache.ProcessRole,
                    [inputSlotOfSecondFragment]),
            ],
            [],
            []);
    }

    private static RenderFragmentReference CreateTemplate()
        => new(
            RenderFragmentKind.OpaqueSource,
            s_bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: [],
            payload: null,
            hitTest: RenderFragmentHitTest.None);

    private static RenderRequestOptions CreateOptions(RenderRequestOwner? owner)
        => new(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            null,
            null,
            1f,
            1f,
            RenderCacheOptions.Disabled,
            FusionMode.Enabled,
            owner);

    private static RecordedRenderGraph Record(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node, cacheEnabled: false);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(CreateOptions(owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        lifecycle.CompleteSuccessfully(false);
        return graph;
    }

    /// <summary>A node whose fragments form a chain, so a replay has real slots to resolve.</summary>
    private sealed class ChainedSourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(
                RenderNodeRecordingCacheTests.CreateSource(bounds));
            RenderFragmentHandle opacity = context.Opacity(source, 0.5f);
            context.Publish(context.Opacity(opacity, 0.25f));
        }
    }

    /// <summary>Records another node from inside its own Process, so the child's commit is absorbed here.</summary>
    private sealed class DrivingNode(RenderNode inner, Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.DisableRenderCache();
            IReadOnlyList<RenderFragmentHandle> outputs = context.RecordSubtree(inner);
            RenderFragmentHandle own = context.OpaqueSource(
                RenderNodeRecordingCacheTests.CreateSource(bounds));
            context.Publish(context.Layer([.. outputs, own], bounds));
        }
    }

    private sealed class PassThroughContainerNode : ContainerRenderNode;

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);

    /// <summary>Hands out one buffer for every rent and poisons it when it comes back.</summary>
    private sealed class SingleBufferPool : ArrayPool<RenderFragmentReference>
    {
        private readonly RenderFragmentReference[] _buffer = new RenderFragmentReference[64];

        public int Rents { get; private set; }

        public override RenderFragmentReference[] Rent(int minimumLength)
        {
            if (minimumLength > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumLength));
            Rents++;
            return _buffer;
        }

        public override void Return(RenderFragmentReference[] array, bool clearArray = false)
        {
            Array.Clear(array);
        }
    }

    /// <summary>Records every rent and return so the discipline can be asserted rather than assumed.</summary>
    private sealed class CountingPool : ArrayPool<RenderFragmentReference>
    {
        private readonly ArrayPool<RenderFragmentReference> _inner = Create();
        private readonly HashSet<RenderFragmentReference[]> _outstanding =
            new(ReferenceEqualityComparer.Instance);

        public int Rents { get; private set; }

        public int Returns { get; private set; }

        public bool ReturnedCleared { get; private set; } = true;

        public IReadOnlyCollection<RenderFragmentReference[]> Outstanding => _outstanding;

        public List<RenderFragmentReference[]> DoubleReturns { get; } = [];

        public override RenderFragmentReference[] Rent(int minimumLength)
        {
            RenderFragmentReference[] array = _inner.Rent(minimumLength);
            Rents++;
            _outstanding.Add(array);
            return array;
        }

        public override void Return(RenderFragmentReference[] array, bool clearArray = false)
        {
            Returns++;
            if (!_outstanding.Remove(array))
                DoubleReturns.Add(array);
            _inner.Return(array, clearArray);
            foreach (RenderFragmentReference? slot in array)
            {
                if (slot is not null)
                    ReturnedCleared = false;
            }
        }
    }
}

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderDescriptionAllocationTests
{
    private const int Iterations = 20000;
    private const int SceneFrames = 40;
    private const int SceneWarmupFrames = 8;

    // Measured steady state is ~255,300 bytes/frame; observed runs span 254,968-255,336, a 0.14% spread. The
    // headroom is for the platform-dependent part of the scene - font fallback for its TextBlock - rather than
    // for measurement noise, so a whole-frame regression above roughly a fifth fails here while per-call
    // regressions are caught by the comparative tests in this fixture.
    private const long SceneBytesPerFrameCeiling = 300_000;

    // The same scene with the render cache warm allocates about 289,400-289,500 bytes/frame. Each machine
    // reports one deterministic value, but not the same one: two machines measured 105 bytes apart, so the
    // figure is platform-specific rather than a property of the scene. The ceiling keeps the same
    // proportional headroom as the cache-disabled budget above, for the same platform-dependent font
    // fallback.
    private const long WarmCacheSceneBytesPerFrameCeiling = 345_000;

    private static readonly object s_explicitKey = new();
    private static readonly PixelSize s_frameSize = new(240, 160);

    [Test]
    public void DefaultStructuralKey_AllocatesNoMoreThanAnExplicitOne()
    {
        Warm();

        long withDefaultKey = MeasureBytesPerCall(structuralKey: null);
        long withExplicitKey = MeasureBytesPerCall(s_explicitKey);

        TestContext.Out.WriteLine($"default key: {withDefaultKey} bytes/call");
        TestContext.Out.WriteLine($"explicit key: {withExplicitKey} bytes/call");
        Assert.That(
            withDefaultKey,
            Is.LessThanOrEqualTo(withExplicitKey),
            "resolving the default structural key runs once per node per frame and must not allocate");
    }

    [Test]
    public void StatePassing_AllocatesNoMoreThanTheCapturingRequestLocalOptOut()
    {
        WarmState();

        long statePassing = MeasureBytesPerCall(static () => CreateWithState(new Rect(0, 0, 4, 4)));
        long requestLocal = MeasureBytesPerCall(static () => CreateRequestLocalCapturing(new Rect(0, 0, 4, 4)));

        TestContext.Out.WriteLine($"state-passing: {statePassing} bytes/call");
        TestContext.Out.WriteLine($"capturing request-local: {requestLocal} bytes/call");
        Assert.That(
            statePassing,
            Is.LessThanOrEqualTo(requestLocal),
            "a static callback plus a state binding must not cost more than the closure it replaced");
    }

    [Test]
    public void ValueTupleState_IsStoredWithoutBoxing()
    {
        WarmState();

        long tupleState = MeasureBytesPerCall(
            static () => TargetCommandDescription.Create(
                (1, 2L, 3f, 4d),
                static (_, _) => { },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None));
        long boxedState = MeasureBytesPerCall(
            static () => TargetCommandDescription.Create(
                (object)(1, 2L, 3f, 4d),
                static (_, _) => { },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None));

        TestContext.Out.WriteLine($"value-tuple state: {tupleState} bytes/call");
        TestContext.Out.WriteLine($"same tuple boxed first: {boxedState} bytes/call");
        Assert.That(
            tupleState,
            Is.LessThan(boxedState),
            "the binding stores the tuple in its own field, so no separate box is allocated");
    }

    [Test]
    public void NestedTupleState_CostsNoMoreThanTheFlatTupleItValidatesAs()
    {
        WarmState();

        long flat = MeasureBytesPerCall(
            static () => TargetCommandDescription.Create(
                (1, 2, 3, 4),
                static (_, _) => { },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None));
        long nested = MeasureBytesPerCall(
            static () => TargetCommandDescription.Create(
                ((1, 2), (3, 4)),
                static (_, _) => { },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None));

        TestContext.Out.WriteLine($"flat tuple state: {flat} bytes/call");
        TestContext.Out.WriteLine($"nested tuple state: {nested} bytes/call");
        Assert.That(
            nested,
            Is.LessThanOrEqualTo(flat),
            "descending through tuple element types happens once per closed state type, not per call");
    }

    [Test]
    [NonParallelizable]
    public void RepresentativeScene_AllocatesWithinItsPerFrameBudget_WithTheCacheDisabled()
    {
        long bytesPerFrame = RenderThread.Dispatcher.Invoke(
            static () => MeasureSceneBytesPerFrame(warmCache: false));

        TestContext.Out.WriteLine($"representative scene, cache disabled: {bytesPerFrame} bytes/frame");
        Assert.That(
            bytesPerFrame,
            Is.LessThan(SceneBytesPerFrameCeiling),
            "recording one frame of the representative scene must stay within its allocation budget");
    }

    [Test]
    [NonParallelizable]
    public void RepresentativeScene_AllocatesWithinItsPerFrameBudget_WithTheCacheActive()
    {
        long bytesPerFrame = RenderThread.Dispatcher.Invoke(
            static () => MeasureSceneBytesPerFrame(warmCache: true));

        TestContext.Out.WriteLine($"representative scene, cache active: {bytesPerFrame} bytes/frame");
        Assert.That(
            bytesPerFrame,
            Is.LessThan(WarmCacheSceneBytesPerFrameCeiling),
            "recording one frame with cache candidates recorded must stay within its allocation budget");
    }

    private static long MeasureSceneBytesPerFrame(bool warmCache)
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
                        TargetDomain = new Rect(default, s_frameSize.ToSize(1)),
                        CacheOptions = warmCache
                            ? RenderCacheOptions.Enabled
                            : RenderCacheOptions.Disabled,
                        Purpose = RenderRequestPurpose.Frame,
                    },
                    TargetFactory = new CpuTargetFactory(),
                });

            var revalidated = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
            for (int frame = 0; frame < SceneWarmupFrames; frame++)
            {
                if (warmCache)
                    IncrementRenderCounts(root, revalidated);
                renderer.Rasterize().Dispose();
            }

            if (warmCache && !root.Cache.CanCache())
            {
                throw new InvalidOperationException(
                    "The warm-cache budget must measure a frame whose nodes are cache candidates.");
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < SceneFrames; frame++)
            {
                if (warmCache)
                    IncrementRenderCounts(root, revalidated);
                renderer.Rasterize().Dispose();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            return (after - before) / SceneFrames;
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }
    }

    /// <summary>
    /// Mirrors the per-frame walk in <c>Renderer.RevalidateAll</c>. Without it
    /// <see cref="RenderNodeCache.CanCache"/> never reaches <see cref="RenderNodeCache.Count"/>, so no
    /// cache candidate is recorded and the whole cache-resolution path stays out of the measurement.
    /// </summary>
    private static void IncrementRenderCounts(RenderNode root, HashSet<RenderNode> revalidated)
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

            current.Cache.IncrementRenderCount();
            current.HasChanges = false;
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

    private static void Warm()
    {
        for (int index = 0; index < 200; index++)
        {
            _ = Create(null);
            _ = Create(s_explicitKey);
        }
    }

    private static void WarmState()
    {
        for (int index = 0; index < 200; index++)
        {
            _ = CreateWithState(new Rect(0, 0, 4, 4));
            _ = CreateRequestLocalCapturing(new Rect(0, 0, 4, 4));
        }
    }

    private static TargetCommandDescription CreateWithState(Rect bounds)
        => TargetCommandDescription.Create(
            bounds,
            static (session, state) => session.Canvas.Use(canvas => canvas.ReplaceAffectedRegion(default)),
            TargetRegion.Region(bounds),
            Rect.Empty,
            RenderHitTestContract.None);

    private static TargetCommandDescription CreateRequestLocalCapturing(Rect bounds)
        => TargetCommandDescription.CreateRequestLocal(
            session => session.Canvas.Use(canvas => canvas.ReplaceAffectedRegion(
                bounds.Width > 0 ? Colors.White : Colors.Black)),
            TargetRegion.Region(bounds),
            Rect.Empty,
            RenderHitTestContract.None);

    private static long MeasureBytesPerCall(object? structuralKey)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = Create(structuralKey);
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Iterations;
    }

    private static long MeasureBytesPerCall(Func<object> create)
    {
        for (int index = 0; index < 200; index++)
            _ = create();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = create();
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Iterations;
    }

    private static TargetCommandDescription Create(object? structuralKey)
        => TargetCommandDescription.CreateRequestLocal(
            Execute,
            TargetRegion.Full,
            Rect.Empty,
            RenderHitTestContract.None,
            structuralKey: structuralKey);

    private static void Execute(TargetCommandSession session)
    {
    }

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
}

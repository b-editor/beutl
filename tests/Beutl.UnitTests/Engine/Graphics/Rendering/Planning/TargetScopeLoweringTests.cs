using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class TargetScopeLoweringTests
{
    private static readonly Rect s_rootDomain = new(0, 0, 100, 60);

    [Test]
    public void RootSequence_ThreadsA_Clear_BThroughOneTargetTokenChain()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new SourceNode(new Rect(0, 0, 20, 20), "root-a"));
        root.AddChild(new ClearRenderNode(Colors.Transparent));
        root.AddChild(new SourceNode(new Rect(40, 10, 20, 20), "root-b"));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        TargetDependencyStep[] steps = compiled.TargetDependencies.Steps.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(steps.Select(static step => step.Kind), Is.EqualTo(new[]
            {
                TargetDependencyKind.Composite,
                TargetDependencyKind.Command,
                TargetDependencyKind.Composite,
            }));
            Assert.That(steps.Select(static step => step.ScopeId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(steps[1].InputToken, Is.EqualTo(steps[0].OutputToken));
            Assert.That(steps[2].InputToken, Is.EqualTo(steps[1].OutputToken));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(s_rootDomain));
            Assert.That(compiled.Measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 60, 30)));
        });
    }

    [Test]
    public void FiniteLayer_ThreadsA_Clear_BLocallyThenCompositesExactlyOnce()
    {
        var domain = new Rect(10, 20, 50, 30);
        using var root = new ContainerRenderNode();
        var layer = new LayerRenderNode(domain);
        layer.AddChild(new SourceNode(new Rect(12, 22, 5, 4), "layer-a"));
        layer.AddChild(new ClearRenderNode(Colors.Transparent));
        layer.AddChild(new SourceNode(new Rect(30, 35, 6, 7), "layer-b"));
        root.AddChild(layer);

        using CompiledRenderRequest compiled = Compile(root, targetDomain: null);
        TargetScopePlan local = FindOwnedScope(compiled, RenderFragmentKind.Layer);
        TargetDependencyStep[] localSteps = compiled.TargetDependencies.Steps
            .Where(step => step.ScopeId == local.Id)
            .ToArray();
        TargetDependencyStep outer = compiled.TargetDependencies.Steps.Single(step =>
            step.Kind == TargetDependencyKind.ScopeComposite);

        Assert.Multiple(() =>
        {
            Assert.That(local.ResolvedDomain, Is.EqualTo(domain));
            Assert.That(local.IsOrderOnly, Is.False);
            Assert.That(localSteps.Select(static step => step.Kind), Is.EqualTo(new[]
            {
                TargetDependencyKind.Composite,
                TargetDependencyKind.Command,
                TargetDependencyKind.Composite,
            }));
            Assert.That(localSteps[1].InputToken, Is.EqualTo(localSteps[0].OutputToken));
            Assert.That(localSteps[2].InputToken, Is.EqualTo(localSteps[1].OutputToken));
            Assert.That(outer.ScopeId, Is.Not.EqualTo(local.Id));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(compiled.Measurement.QueryBounds, Is.EqualTo(new Rect(12, 22, 24, 20)));
        });
    }

    [Test]
    public void TransformedFullTargetLayer_ResolvesAgainstMappedCurrentTargetDomain()
    {
        using var root = new ContainerRenderNode();
        var transform = new TransformRenderNode(
            Matrix.CreateTranslation(10, 0),
            TransformOperator.Prepend);
        var isolation = new LayerRenderNode(default);
        isolation.AddChild(new ClearRenderNode(Colors.White));
        transform.AddChild(isolation);
        root.AddChild(transform);

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        TargetScopePlan transformed = FindOwnedScope(compiled, RenderFragmentKind.TargetScope);
        TargetScopePlan isolated = FindOwnedScope(compiled, RenderFragmentKind.TargetLayerScope);
        RenderFragmentReference transformedReference = References(compiled.Graph)[transformed.OwnerFragmentId!.Value];

        Assert.Multiple(() =>
        {
            Assert.That(transformed.ResolvedDomain, Is.EqualTo(new Rect(-10, 0, 100, 60)));
            Assert.That(isolated.ResolvedDomain, Is.EqualTo(new Rect(-10, 0, 100, 60)));
            Assert.That(isolated.ParentId, Is.EqualTo(transformed.Id));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(s_rootDomain));
            Assert.That(compiled.Measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(compiled.ExecutionTargetBounds, Is.EqualTo(s_rootDomain));
            Assert.That(
                compiled.Regions.GetFragmentRequirement(transformedReference).Resolve(s_rootDomain),
                Is.EqualTo(s_rootDomain));
        });

        var factory = new CpuTargetFactory();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = factory,
                UseRenderCache = false,
            });
        using RenderNodeRasterization raster = renderer.Rasterize();
        Bitmap bitmap = raster.Bitmap!;
        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(AlphaAt(bitmap, 0, 30), Is.GreaterThan(0.99f),
                "The inverse-mapped local Full must include the root's left edge.");
            Assert.That(AlphaAt(bitmap, 99, 30), Is.GreaterThan(0.99f),
                "The local Full must still include the root's right edge.");
        });
    }

    [Test]
    public void ClippedTransformedFullTargetLayer_ResolvesAgainstTheClippedLocalDomain()
    {
        var clipDomain = new Rect(20, 0, 30, 60);
        using var root = new RectClipRenderNode(clipDomain, ClipOperation.Intersect);
        var transform = new TransformRenderNode(
            Matrix.CreateTranslation(10, 0),
            TransformOperator.Prepend);
        var isolation = new LayerRenderNode(default);
        isolation.AddChild(new ClearRenderNode(Colors.White));
        transform.AddChild(isolation);
        root.AddChild(transform);

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = References(compiled.Graph);
        TargetScopePlan[] mappedScopes = compiled.TargetDependencies.Scopes
            .Where(scope => scope.OwnerFragmentId is { } owner
                            && references[owner].Kind == RenderFragmentKind.TargetScope)
            .ToArray();
        TargetScopePlan clipped = mappedScopes.Single(scope =>
            scope.ResolvedDomain == clipDomain);
        TargetScopePlan transformed = mappedScopes.Single(scope =>
            scope.ParentId == clipped.Id);
        TargetScopePlan isolated = FindOwnedScope(compiled, RenderFragmentKind.TargetLayerScope);
        RenderFragmentReference transformedReference = references[transformed.OwnerFragmentId!.Value];
        var localDomain = new Rect(10, 0, 30, 60);

        Assert.Multiple(() =>
        {
            Assert.That(transformed.ResolvedDomain, Is.EqualTo(localDomain));
            Assert.That(isolated.ResolvedDomain, Is.EqualTo(localDomain));
            Assert.That(isolated.ParentId, Is.EqualTo(transformed.Id));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(clipDomain));
            Assert.That(compiled.Measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(compiled.ExecutionTargetBounds, Is.EqualTo(clipDomain));
            Assert.That(
                compiled.Regions.GetFragmentRequirement(transformedReference).Resolve(clipDomain),
                Is.EqualTo(clipDomain));
        });

        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using RenderNodeRasterization raster = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(clipDomain));
            Assert.That(AlphaAt(raster.Bitmap!, 0, 30), Is.GreaterThan(0.99f));
            Assert.That(AlphaAt(raster.Bitmap!, 29, 30), Is.GreaterThan(0.99f));
        });
    }

    [Test]
    public void TransformedRootReadback_MapsItsLocalAccessIntoTheRootExecutionTarget()
    {
        var localAccess = new Rect(5, 7, 20, 11);
        using var root = new ContainerRenderNode();
        root.AddChild(new OrderOnlyCommandNode());
        var transform = new TransformRenderNode(
            Matrix.CreateTranslation(10, 0),
            TransformOperator.Prepend);
        transform.AddChild(new ReadbackCommandNode(localAccess));
        root.AddChild(transform);

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference readback = References(compiled.Graph).Values.Single(reference =>
            reference.Payload is TargetCommandRenderFragmentPayload payload
            && payload.Description.Access == TargetAccess.Readback);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.SelectedOutputBounds, Is.EqualTo(Rect.Empty));
            Assert.That(
                compiled.Regions.GetTargetAccessRequirement(readback).Resolve(s_rootDomain),
                Is.EqualTo(localAccess));
            Assert.That(
                compiled.ExecutionTargetBounds,
                Is.EqualTo(new Rect(15, 7, 20, 11)));
        });
    }

    [TestCase("opacity")]
    [TestCase("blend")]
    [TestCase("opacity-mask")]
    public void TypedTargetStateScope_PreservesTargetOnlyFullWrite(string scopeKind)
    {
        using ContainerRenderNode root = scopeKind switch
        {
            "opacity" => new OpacityRenderNode(1),
            "blend" => new BlendModeRenderNode(BlendMode.SrcOver),
            "opacity-mask" => new OpacityMaskRenderNode(
                Brushes.Resource.White,
                s_rootDomain,
                invert: false),
            _ => throw new ArgumentOutOfRangeException(nameof(scopeKind)),
        };
        root.AddChild(new ClearRenderNode(Colors.White));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });

        using RenderNodeRasterization raster = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(AlphaAt(raster.Bitmap!, 0, 30), Is.GreaterThan(0.99f));
            Assert.That(AlphaAt(raster.Bitmap!, 99, 30), Is.GreaterThan(0.99f));
        });
    }

    [Test]
    public void RootFullTargetLayer_ReplaysAndCompositesItsLocalTarget()
    {
        using var root = new EmptyTargetLayerNode(TargetRegion.Full);
        root.AddChild(new FullCommandNode(Colors.White));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });

        using RenderNodeRasterization raster = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(AlphaAt(raster.Bitmap!, 0, 30), Is.GreaterThan(0.99f));
            Assert.That(AlphaAt(raster.Bitmap!, 99, 30), Is.GreaterThan(0.99f));
        });
    }

    [Test]
    public void EmptyTargetLayer_RemainsOrderOnlyWithoutAChainOrPixelSteps()
    {
        using var root = new ContainerRenderNode();
        var empty = new EmptyTargetLayerNode();
        empty.AddChild(new SourceNode(new Rect(0, 0, 20, 20), "empty-source"));
        empty.AddChild(new ClearRenderNode(Colors.Transparent));
        root.AddChild(empty);
        root.AddChild(new SourceNode(new Rect(30, 0, 10, 10), "after-empty"));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        TargetScopePlan scope = FindOwnedScope(compiled, RenderFragmentKind.TargetLayerScope);
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = References(compiled.Graph);

        Assert.Multiple(() =>
        {
            Assert.That(scope.ResolvedDomain, Is.EqualTo(Rect.Empty));
            Assert.That(scope.IsOrderOnly, Is.True);
            Assert.That(compiled.TargetDependencies.Steps.Count(step => step.ScopeId == scope.Id), Is.Zero);
            Assert.That(compiled.TargetDependencies.Steps.Length, Is.EqualTo(1));
            Assert.That(
                references[compiled.TargetDependencies.Steps.Single().FragmentId].Kind,
                Is.EqualTo(RenderFragmentKind.OpaqueSource));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(new Rect(30, 0, 10, 10)));
            Assert.That(compiled.Measurement.QueryBounds, Is.EqualTo(new Rect(30, 0, 10, 10)));
        });
    }

    [Test]
    public void FullWithoutAnOwningDomain_FailsDuringLowering_NotDuringRecording()
    {
        using var fullCommand = new FullCommandNode();
        using var owner = new RenderRequestOwner();
        var request = new RenderRequest(Options(targetDomain: null, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(fullCommand);

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestCompiler().Compile(request, graph));

        Assert.That(error!.Message, Does.Contain("finite").And.Contain("target domain").IgnoreCase);
    }

    [Test]
    public void RequestedRegionDoesNotSupplyMissingTargetDomain_ButFiniteLayerDoes()
    {
        using var command = new FullCommandNode();
        Assert.That(
            () => Compile(command, targetDomain: null, requestedRegion: new Rect(2, 3, 4, 5)),
            Throws.TypeOf<InvalidOperationException>());

        using var root = new ContainerRenderNode();
        var finite = new LayerRenderNode(new Rect(10, 20, 30, 40));
        var nestedFull = new EmptyTargetLayerNode(TargetRegion.Full);
        nestedFull.AddChild(new ClearRenderNode(Colors.Transparent));
        finite.AddChild(nestedFull);
        root.AddChild(finite);

        using CompiledRenderRequest compiled = Compile(root, targetDomain: null);
        TargetScopePlan isolated = FindOwnedScope(compiled, RenderFragmentKind.TargetLayerScope);
        Assert.That(isolated.ResolvedDomain, Is.EqualTo(new Rect(10, 20, 30, 40)));
    }

    [Test]
    public void FullClear_UsesOutputDomainButKeepsQueryAndHitTestingEmpty()
    {
        var domain = new Rect(10, 20, 40, 30);
        using var root = new FullCommandNode(Colors.White);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = domain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(renderer.HitTest(new Point(20, 25)), Is.False);
        });

        using RenderNodeRasterization raster = renderer.Rasterize();
        Assert.That(AlphaAt(raster.Bitmap!, 20, 15), Is.GreaterThan(0.99f));
    }

    [Test]
    public void Rasterize_PreservesShiftedSelection_AndEmptySelectionDoesNotAllocate()
    {
        var factory = new CpuTargetFactory();
        var shifted = new Rect(10.25f, 20.25f, 3.5f, 2.5f);
        using var root = new SourceNode(shifted, "shifted-raster", execute: true);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                OutputScale = 2,
                TargetFactory = factory,
                UseRenderCache = false,
            });

        using (RenderNodeRasterization raster = renderer.Rasterize())
        {
            Assert.Multiple(() =>
            {
                Assert.That(raster.Bounds, Is.EqualTo(shifted));
                Assert.That(raster.Bitmap, Is.Not.Null);
                Assert.That(raster.OutputScale, Is.EqualTo(2));
            });
        }

        int allocationsAfterShifted = factory.AllocationCount;
        var emptySelection = new Rect(70, 80, 0, 5);
        using var emptyRenderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                RequestedRegion = emptySelection,
                TargetFactory = factory,
                UseRenderCache = false,
            });
        using RenderNodeRasterization empty = emptyRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(empty.Bounds, Is.EqualTo(emptySelection));
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.Bitmap, Is.Null);
            Assert.That(factory.AllocationCount, Is.EqualTo(allocationsAfterShifted));
        });
    }

    private static CompiledRenderRequest Compile(
        RenderNode root,
        Rect? targetDomain,
        Rect? requestedRegion = null)
    {
        var request = new RenderRequest(Options(targetDomain, requestedRegion));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        return new RenderRequestCompiler().Compile(request, graph);
    }

    private static RenderRequestOptions Options(
        Rect? targetDomain,
        Rect? requestedRegion = null,
        RenderRequestOwner? owner = null)
        => new(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain,
            requestedRegion,
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner);

    private static TargetScopePlan FindOwnedScope(
        CompiledRenderRequest compiled,
        RenderFragmentKind kind)
    {
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = References(compiled.Graph);
        return compiled.TargetDependencies.Scopes.Single(scope =>
            scope.OwnerFragmentId is { } owner && references[owner].Kind == kind);
    }

    private static IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> References(
        RecordedRenderGraph graph)
        => graph.Fragments.ToDictionary(
            static fragment => fragment.Id,
            static fragment => (RenderFragmentReference)fragment.Payload!);

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
    }

    private sealed class FullCommandNode(Color? color = null) : RenderNode
    {
        private readonly Color _color = color ?? Colors.Transparent;

        public override void Process(RenderNodeContext context)
        {
            Color color = _color;
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session => session.Canvas.Use(canvas => canvas.Clear(color)),
                    TargetRegion.Full,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: typeof(FullCommandNode))));
        }
    }

    private sealed class ReadbackCommandNode(Rect region) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session => session.UseSnapshot(static _ => { }),
                    TargetRegion.Region(region),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback,
                    structuralKey: typeof(ReadbackCommandNode))));
        }
    }

    private sealed class OrderOnlyCommandNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => { },
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: typeof(OrderOnlyCommandNode))));
        }
    }

    private sealed class EmptyTargetLayerNode(TargetRegion? region = null) : ContainerRenderNode
    {
        private readonly TargetRegion _region = region ?? TargetRegion.Empty;

        public override void Process(RenderNodeContext context)
            => context.Publish(context.TargetLayerScope(context.Inputs, _region));
    }

    private sealed class SourceNode(Rect bounds, string key, bool execute = false) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    if (!execute)
                        throw new AssertionException("Metadata and lowering must not execute source callbacks.");

                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(SourceNode), key));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int AllocationCount { get; private set; }

        public RenderTarget Create(PixelSize deviceSize)
        {
            AllocationCount++;
            return new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
        }
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

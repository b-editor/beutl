using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class GpuPassFusionScaleRegionTests
{
    private static readonly Rect s_domain = new(0, 0, 96, 64);

    [Test]
    public void MixedVectorBitmapAndTextInputs_ResolveDensestSupplyThenMaximumWorkingScale()
    {
        using var root = new MixedDensityNode();
        using var owner = new RenderRequestOwner();
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Bounds,
            targetDomain: s_domain,
            outputScale: 1,
            maxWorkingScale: 2.5f,
            owner: owner));
        using (request)
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
            RenderFragmentReference combined = graph.Fragments
                .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
                .Single(static reference => reference.Kind == RenderFragmentKind.OpaqueCombine);

            Assert.Multiple(() =>
            {
                Assert.That(combined.Inputs, Has.Length.EqualTo(4));
                Assert.That(combined.Inputs.Count(static input => input.EffectiveScale.IsUnbounded),
                    Is.EqualTo(2), "vector geometry and text must remain re-rasterizable");
                Assert.That(
                    combined.Inputs
                        .Where(static input => !input.EffectiveScale.IsUnbounded)
                        .Select(static input => input.EffectiveScale.Value),
                    Is.EquivalentTo(new[] { 0.5f, 2.5f }),
                    "each concrete source is capped as it is recorded before the combined boundary resolves");
                Assert.That(combined.EffectiveScale.IsUnbounded, Is.False);
                Assert.That(combined.EffectiveScale.Value, Is.EqualTo(2.5f),
                    "the dense bitmap supply wins before the request ceiling is applied");
            });
        }
    }

    [Test]
    public void CompleteBoundsClamp_PrecedesRequestedRegionAndNeverExceedsTheAxisBudget()
    {
        var completeBounds = new Rect(123.25f, 9, 10_000.25f, 12);
        var requestedRegion = new Rect(9_000, 9, 8, 8);
        using RenderNode root = ScaleRecordingTestHelper.Source(EffectiveScale.At(4), completeBounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = completeBounds,
                RequestedRegion = requestedRegion,
                OutputScale = 1,
                MaxWorkingScale = float.PositiveInfinity,
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(completeBounds, 4);

        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.LessThan(4), "the fixture must exercise the 16,384-axis clamp");
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
            Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expected));
            Assert.That(
                Math.Ceiling(completeBounds.Width * measurement.EffectiveScale.Value),
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                measurement.EffectiveScale.Value,
                Is.LessThan(RenderScaleUtilities.ClampWorkingScaleToBufferBudget(requestedRegion, 4)),
                "a late ROI crop must not raise a density already clamped against complete bounds");
        });
    }

    [Test]
    public void RequestedRegionOutsideRootOutputExtent_RasterizesWithoutABitmap()
    {
        var outputBounds = new Rect(10, 20, 30, 40);
        using RenderNode root = ScaleRecordingTestHelper.Source(EffectiveScale.At(1), outputBounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 200, 300),
                RequestedRegion = new Rect(100, 200, 25, 20),
                OutputScale = 1,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(Rect.Empty));
            Assert.That(rasterization.Bitmap, Is.Null);
        });
    }

    [Test]
    public void ShiftedGuardedCallback_LateBindsRequiredAndDeviceRegionsWithoutRunningDuringMeasure()
    {
        var declaredBounds = new Rect(10.25f, 20.5f, 8, 6);
        var requestedRegion = new Rect(12.25f, 22.5f, 3, 2);
        using var node = new ShiftedCallbackNode(declaredBounds);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 40, 40),
                RequestedRegion = requestedRegion,
                OutputScale = 1,
                MaxWorkingScale = 4,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        Assert.That(node.CallbackCount, Is.Zero, "metadata resolution must not execute guarded work");
        Assert.That(measurement.OutputBounds, Is.EqualTo(declaredBounds));

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        PixelRect expectedDeviceBounds = PixelRect.FromRect(requestedRegion, 2);
        var expectedOrigin = new Point(expectedDeviceBounds.X / 2f, expectedDeviceBounds.Y / 2f);

        Assert.Multiple(() =>
        {
            Assert.That(node.CallbackCount, Is.EqualTo(1));
            Assert.That(node.ObservedOutputBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.ObservedRequiredRegion, Is.EqualTo(requestedRegion));
            Assert.That(node.ObservedSessionDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(node.ObservedCanvasBounds, Is.EqualTo(requestedRegion));
            Assert.That(node.ObservedCanvasDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(node.ObservedCanvasOrigin, Is.EqualTo(expectedOrigin));
            Assert.That(node.ObservedDensity, Is.EqualTo(2));
            Assert.That(rasterization.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    public void TypedGeometryShaderAndTargetScope_ReceiveTheCroppedRuntimeRequirement()
    {
        var declaredBounds = new Rect(10, 20, 12, 8);
        var requestedRegion = new Rect(12, 22, 4, 3);
        using var node = new TypedValueRoiNode(declaredBounds);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 40, 40),
                RequestedRegion = requestedRegion,
                OutputScale = 2,
                MaxWorkingScale = 4,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        Assert.That(node.TotalCallbackCount, Is.Zero);
        Assert.That(measurement.OutputBounds, Is.EqualTo(declaredBounds));

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        PixelRect expectedDeviceBounds = PixelRect.FromRect(requestedRegion, 2);

        Assert.Multiple(() =>
        {
            Assert.That(node.GeometryOutputBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.GeometryRequiredRegion, Is.EqualTo(requestedRegion));
            Assert.That(node.GeometryDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(node.GeometryCanvasBounds, Is.EqualTo(requestedRegion));
            Assert.That(node.ShaderInputBounds, Is.EqualTo(requestedRegion));
            Assert.That(node.ShaderOutputBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.ShaderRequiredRegion, Is.EqualTo(requestedRegion));
            Assert.That(node.ShaderDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(node.TargetScopeOutputBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.TargetScopeRequiredRegion, Is.EqualTo(requestedRegion));
            Assert.That(node.TargetScopeCanvasBounds, Is.EqualTo(requestedRegion));
            Assert.That(node.TargetScopeCanvasDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(rasterization.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    public void TargetReadback_ExpandsThePrecedingSnapshotAndCanvasToItsDeclaredReadRegion()
    {
        var declaredBounds = new Rect(10, 20, 12, 8);
        var requestedRegion = new Rect(12, 22, 4, 3);
        using var node = new TargetReadbackRoiNode(declaredBounds);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 40, 40),
                RequestedRegion = requestedRegion,
                OutputScale = 2,
                MaxWorkingScale = 4,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        Assert.That(node.CallbackCount, Is.Zero);
        Assert.That(measurement.OutputBounds, Is.EqualTo(declaredBounds));

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        PixelRect expectedDeviceBounds = PixelRect.FromRect(declaredBounds, 2);

        Assert.Multiple(() =>
        {
            Assert.That(node.CallbackCount, Is.EqualTo(1));
            Assert.That(node.SourceRequiredRegion, Is.EqualTo(declaredBounds));
            Assert.That(node.AffectedBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.RequiredRegion, Is.EqualTo(declaredBounds));
            Assert.That(node.CanvasBounds, Is.EqualTo(declaredBounds));
            Assert.That(node.CanvasDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(node.SnapshotSize, Is.EqualTo(expectedDeviceBounds.Size));
            Assert.That(node.SnapshotOpaquePixelCount,
                Is.EqualTo(expectedDeviceBounds.Width * expectedDeviceBounds.Height));
            Assert.That(node.SnapshotCornerAlpha, Is.GreaterThan(0.99f),
                "the target-read apron must contain the preceding target pixels, not only an expanded allocation");
            Assert.That(rasterization.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    public void RenderWithTargetReadApron_CommitsOnlyTheRequestedRegionToTheBorrowedTarget()
    {
        var declaredBounds = new Rect(10, 20, 12, 8);
        var requestedRegion = new Rect(12, 22, 4, 3);
        using var node = new TargetReadbackRoiNode(declaredBounds);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                RequestedRegion = requestedRegion,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });
        using var target = new CpuRenderTarget(80, 80);
        using var canvas = new ImmediateCanvas(
            target,
            density: 2,
            logicalSize: new Size(40, 40));
        canvas.Clear(Colors.Red);

        renderer.Render(canvas);
        using Bitmap completed = target.Snapshot();
        (float outsideRed, float outsideBlue) = RedBlueAt(completed, 21, 41);
        (float insideRed, float insideBlue) = RedBlueAt(completed, 26, 46);

        Assert.Multiple(() =>
        {
            Assert.That(node.SourceRequiredRegion, Is.EqualTo(declaredBounds));
            Assert.That(node.SnapshotCornerBlue, Is.GreaterThan(node.SnapshotCornerRed),
                "the readback apron must observe the preceding blue graph output, not the borrowed red target");
            Assert.That(outsideRed, Is.GreaterThan(outsideBlue),
                "pixels outside the final commit crop must retain the borrowed target content");
            Assert.That(insideBlue, Is.GreaterThan(insideRed),
                "pixels inside the final commit crop must receive the completed graph output");
        });
    }

    [TestCase(CaptureContainer.FiniteLayer)]
    [TestCase(CaptureContainer.TargetLayerScope)]
    public void DeclaredTargetCaptureResamplesWhileBackdropLateBindsToDenserScopeAndCacheIdentity(
        CaptureContainer container)
    {
        using var node = new CaptureDensityNode(container);

        RenderFragmentOutputIdentity declaredAtOne = RecordCaptureIdentity(node, outputScale: 1, builtIn: false);
        RenderFragmentOutputIdentity declaredAtTwo = RecordCaptureIdentity(node, outputScale: 2, builtIn: false);
        RenderFragmentOutputIdentity backdropIdentity = RecordCaptureIdentity(node, outputScale: 1, builtIn: true);

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_domain,
                OutputScale = 1,
                MaxWorkingScale = 2,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(node.PublicCaptureInputDensity, Is.EqualTo(1),
                "a public capture uses its no-input, output-derived declared density");
            Assert.That(node.BuiltInCaptureInputDensity, Is.EqualTo(2),
                "the engine backdrop must bind to the actual denser owning target");
            Assert.That(node.CommittedBackdropDensity, Is.EqualTo(2));
            Assert.That(declaredAtOne, Is.Not.EqualTo(declaredAtTwo),
                "declared capture density must participate in the output-cache identity");
            Assert.That(declaredAtOne, Is.Not.EqualTo(backdropIdentity),
                "a late-bound backdrop is request-local and cannot alias a public declared-density capture");
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AntialiasedThinStroke_CurrentPixelBoundaryPreservesEdgeCoverage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var node = new ThinStrokeShaderNode();
            GpuPassFusionParityResult result = GpuPassFusionSameProcessParityHarness.AssertParity(
                mode => RenderThinStroke(node, mode),
                new PixelRect(18, 16, 156, 76));

            Assert.Multiple(() =>
            {
                Assert.That(result.AaEdge, Is.Not.Null);
                Assert.That(result.AaEdge!.Value.EdgeBandMeanError,
                    Is.LessThanOrEqualTo(GpuPassFusionSameProcessParityHarness.MaximumAaEdgeMeanError));
                Assert.That(result.AaEdge.Value.MaximumError.Maximum,
                    Is.LessThanOrEqualTo(GpuPassFusionSameProcessParityHarness.MaximumAaEdgeChannelError));
            });
        });
    }

    private static RenderFragmentOutputIdentity RecordCaptureIdentity(
        RenderNode root,
        float outputScale,
        bool builtIn)
    {
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_domain,
            outputScale: outputScale,
            maxWorkingScale: 2,
            owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        RenderFragmentReference reference = graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Single(item => item.Kind == (builtIn
                ? RenderFragmentKind.BuiltInBackdropCapture
                : RenderFragmentKind.TargetCapture));
        return RenderFragmentOutputIdentity.Create(reference, request.Id);
    }

    private static Bitmap RenderThinStroke(RenderNode node, FusionMode mode)
    {
        const int width = 192;
        const int height = 108;
        using RenderTarget target = RenderTarget.Create(width, height)
            ?? throw new InvalidOperationException("Could not allocate the thin-stroke target.");
        using (var canvas = new ImmediateCanvas(target, 1, 2, new Size(width, height)))
        {
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    TargetDomain = new Rect(0, 0, width, height),
                    OutputScale = 1,
                    MaxWorkingScale = 2,
                    UseRenderCache = false,
                    FusionMode = mode,
                });
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    public enum CaptureContainer
    {
        FiniteLayer,
        TargetLayerScope,
    }

    private sealed class MixedDensityNode : RenderNode
    {
        private readonly RenderNode _lowDensity = ScaleRecordingTestHelper.Source(EffectiveScale.At(0.5f));
        private readonly RenderNode _highDensity = ScaleRecordingTestHelper.Source(EffectiveScale.At(4));
        private readonly RectangleRenderNode _vector = new(
            new Rect(4, 6, 60, 38),
            Brushes.Resource.White,
            null);
        private readonly FormattedText _text;
        private readonly TextRenderNode _textNode;

        public MixedDensityNode()
        {
            Typeface typeface = TypefaceProvider.Typeface();
            _text = new FormattedText
            {
                Font = typeface.FontFamily,
                Style = typeface.Style,
                Weight = typeface.Weight,
                Size = 18,
                Text = "density",
            };
            _textNode = new TextRenderNode(_text, Brushes.Resource.White, null);
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle[] inputs =
            [
                context.RecordNode(_lowDensity, [])[0],
                context.RecordNode(_highDensity, [])[0],
                context.RecordNode(_vector, [])[0],
                context.RecordNode(_textNode, [])[0],
            ];
            context.Publish(context.OpaqueCombine(inputs, CreateCombineDescription(typeof(MixedDensityNode))));
        }

        protected override void OnDispose(bool disposing)
        {
            _lowDensity.Dispose();
            _highDensity.Dispose();
            _vector.Dispose();
            _textNode.Dispose();
            _text.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class ShiftedCallbackNode(Rect bounds) : RenderNode
    {
        public int CallbackCount { get; private set; }

        public Rect ObservedOutputBounds { get; private set; }

        public Rect ObservedRequiredRegion { get; private set; }

        public PixelRect ObservedSessionDeviceBounds { get; private set; }

        public Rect ObservedCanvasBounds { get; private set; }

        public PixelRect ObservedCanvasDeviceBounds { get; private set; }

        public Point ObservedCanvasOrigin { get; private set; }

        public float ObservedDensity { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                execute: session =>
                {
                    CallbackCount++;
                    ObservedOutputBounds = session.OutputBounds;
                    ObservedRequiredRegion = session.RequiredRegion;
                    ObservedSessionDeviceBounds = session.DeviceBounds;
                    ObservedDensity = session.WorkingScale;
                    using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
                    ObservedCanvasBounds = output.Canvas.LogicalBounds;
                    ObservedCanvasDeviceBounds = output.Canvas.DeviceBounds;
                    ObservedCanvasOrigin = output.Canvas.LogicalOrigin;
                    output.Canvas.Use(static _ => { });
                    session.Publish(output);
                },
                bounds: RenderOperationBoundsContract.Source(bounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Custom(static _ => 2, typeof(ShiftedCallbackNode)),
                structuralKey: typeof(ShiftedCallbackNode),
                runtimeIdentity: new RenderRuntimeIdentity(bounds));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class TypedValueRoiNode(Rect bounds) : RenderNode
    {
        public int TotalCallbackCount { get; private set; }

        public Rect GeometryOutputBounds { get; private set; }

        public Rect GeometryRequiredRegion { get; private set; }

        public PixelRect GeometryDeviceBounds { get; private set; }

        public Rect GeometryCanvasBounds { get; private set; }

        public Rect ShaderInputBounds { get; private set; }

        public Rect ShaderOutputBounds { get; private set; }

        public Rect ShaderRequiredRegion { get; private set; }

        public PixelRect ShaderDeviceBounds { get; private set; }

        public Rect TargetScopeOutputBounds { get; private set; }

        public Rect TargetScopeRequiredRegion { get; private set; }

        public Rect TargetScopeCanvasBounds { get; private set; }

        public PixelRect TargetScopeCanvasDeviceBounds { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateRoiSourceDescription(
                bounds,
                typeof(TypedValueRoiNode)));
            GeometryDescription geometry = GeometryDescription.Create(
                session =>
                {
                    TotalCallbackCount++;
                    GeometryOutputBounds = session.OutputBounds;
                    GeometryRequiredRegion = session.RequiredRegion;
                    GeometryDeviceBounds = session.DeviceBounds;
                    GeometryCanvasBounds = session.Canvas.LogicalBounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: typeof(TypedValueRoiNode),
                runtimeIdentity: new RenderRuntimeIdentity("typed-roi-geometry"));
            RenderFragmentHandle current = context.Geometry(source, geometry);
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                bindings => bindings.Uniform(
                    "gain",
                    1f,
                    (writer, value, execution) =>
                    {
                        TotalCallbackCount++;
                        ShaderInputBounds = execution.InputBounds;
                        ShaderOutputBounds = execution.OutputBounds;
                        ShaderRequiredRegion = execution.RequiredRegion;
                        ShaderDeviceBounds = execution.DeviceBounds;
                        writer.Set(value);
                    },
                    structuralKey: typeof(TypedValueRoiNode),
                    runtimeIdentity: new RenderRuntimeIdentity("typed-roi-shader")));
            current = context.Shader(current, shader);
            TargetScopeDescription scope = TargetScopeDescription.Create(
                session =>
                {
                    TotalCallbackCount++;
                    TargetScopeOutputBounds = session.OutputBounds;
                    TargetScopeRequiredRegion = session.RequiredRegion;
                    TargetScopeCanvasBounds = session.Canvas.LogicalBounds;
                    TargetScopeCanvasDeviceBounds = session.Canvas.DeviceBounds;
                    session.Canvas.Use(_ => session.ReplayInput());
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: typeof(TypedValueRoiNode),
                runtimeIdentity: new RenderRuntimeIdentity("typed-roi-scope"));
            context.Publish(context.TargetScope(current, scope));
        }
    }

    private sealed class TargetReadbackRoiNode(Rect bounds) : RenderNode
    {
        public int CallbackCount { get; private set; }

        public Rect SourceRequiredRegion { get; private set; }

        public Rect AffectedBounds { get; private set; }

        public Rect RequiredRegion { get; private set; }

        public Rect CanvasBounds { get; private set; }

        public PixelRect CanvasDeviceBounds { get; private set; }

        public PixelSize SnapshotSize { get; private set; }

        public float SnapshotCornerAlpha { get; private set; }

        public float SnapshotCornerRed { get; private set; }

        public float SnapshotCornerBlue { get; private set; }

        public int SnapshotOpaquePixelCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    SourceRequiredRegion = session.RequiredRegion;
                    using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static _ => 2, typeof(TargetReadbackRoiNode)),
                structuralKey: typeof(TargetReadbackRoiNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(TargetReadbackRoiNode))));
            context.Publish(source);
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session =>
                    {
                        CallbackCount++;
                        AffectedBounds = session.AffectedBounds;
                        RequiredRegion = session.RequiredRegion;
                        CanvasBounds = session.Canvas.LogicalBounds;
                        CanvasDeviceBounds = session.Canvas.DeviceBounds;
                        session.UseSnapshot(bitmap =>
                        {
                            SnapshotSize = new PixelSize(bitmap.Width, bitmap.Height);
                            Span<ushort> firstRow = bitmap.GetRow<ushort>(0);
                            SnapshotCornerRed = (float)BitConverter.UInt16BitsToHalf(firstRow[0]);
                            SnapshotCornerBlue = (float)BitConverter.UInt16BitsToHalf(firstRow[2]);
                            SnapshotCornerAlpha = (float)BitConverter.UInt16BitsToHalf(firstRow[3]);
                            for (int y = 0; y < bitmap.Height; y++)
                            {
                                Span<ushort> row = bitmap.GetRow<ushort>(y);
                                for (int x = 0; x < bitmap.Width; x++)
                                {
                                    if ((float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]) > 0.99f)
                                        SnapshotOpaquePixelCount++;
                                }
                            }
                        });
                    },
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback,
                    structuralKey: typeof(TargetReadbackRoiNode),
                    runtimeIdentity: new RenderRuntimeIdentity("target-readback-roi"))));
        }
    }

    private static (float Red, float Blue) RedBlueAt(Bitmap bitmap, int x, int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        int offset = x * 4;
        return (
            (float)BitConverter.UInt16BitsToHalf(row[offset]),
            (float)BitConverter.UInt16BitsToHalf(row[offset + 2]));
    }

    private static OpaqueRenderDescription CreateRoiSourceDescription(Rect bounds, object key)
        => OpaqueRenderDescription.Create(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Custom(static _ => 2, key),
            structuralKey: key,
            runtimeIdentity: new RenderRuntimeIdentity(key));

    private sealed class CaptureDensityNode(CaptureContainer container)
        : RenderNode, IBuiltInBackdropCaptureSink
    {
        private readonly RenderNode _localSource = ScaleRecordingTestHelper.Source(EffectiveScale.Unbounded, s_domain);
        private readonly RenderNode _denseSource = ScaleRecordingTestHelper.Source(EffectiveScale.At(2), s_domain);
        private Bitmap? _committedBackdrop;

        public float PublicCaptureInputDensity { get; private set; }

        public float BuiltInCaptureInputDensity { get; private set; }

        public float CommittedBackdropDensity { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.RecordNode(_localSource, [])[0];
            RenderFragmentHandle publicCapture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(s_domain),
                s_domain,
                RenderHitTestContract.None,
                RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle publicReplay = context.ContributeValues(
                context.OpaqueMap(publicCapture, CreateCaptureObserver(CaptureKind.Public)));

            RenderFragmentHandle builtInCapture = context.BuiltInBackdropCapture(this);
            RenderFragmentHandle builtInReplay = context.ContributeValues(
                context.OpaqueMap(builtInCapture, CreateCaptureObserver(CaptureKind.BuiltIn)));
            RenderFragmentHandle[] localInputs =
                [source, publicCapture, publicReplay, builtInCapture, builtInReplay];

            RenderFragmentHandle layer = container switch
            {
                CaptureContainer.FiniteLayer => context.Layer(localInputs, s_domain),
                CaptureContainer.TargetLayerScope => context.Layer(
                    [context.TargetLayerScope(localInputs, TargetRegion.Region(s_domain))],
                    s_domain),
                _ => throw new ArgumentOutOfRangeException(),
            };
            RenderFragmentHandle dense = context.RecordNode(_denseSource, [])[0];
            context.Publish(context.OpaqueCombine(
                [layer, dense],
                CreateCombineDescription(typeof(CaptureDensityNode))));
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
        {
            _committedBackdrop?.Dispose();
            _committedBackdrop = bitmap;
            CommittedBackdropDensity = density;
        }

        protected override void OnDispose(bool disposing)
        {
            _committedBackdrop?.Dispose();
            _committedBackdrop = null;
            _localSource.Dispose();
            _denseSource.Dispose();
            base.OnDispose(disposing);
        }

        private OpaqueRenderDescription CreateCaptureObserver(CaptureKind kind)
        {
            return OpaqueRenderDescription.Create(
                execute: session =>
                {
                    float density = session.Inputs.Single().EffectiveScale.Value;
                    if (kind == CaptureKind.Public)
                        PublicCaptureInputDensity = density;
                    else
                        BuiltInCaptureInputDensity = density;

                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                bounds: RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.AnyInput,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.PreserveInputSupply,
                structuralKey: new CaptureObserverIdentity(kind),
                runtimeIdentity: new RenderRuntimeIdentity(new CaptureObserverIdentity(kind)));
        }

        private enum CaptureKind
        {
            Public,
            BuiltIn,
        }

        private readonly record struct CaptureObserverIdentity(CaptureKind Kind);
    }

    private sealed class ThinStrokeShaderNode : RenderNode
    {
        private static readonly ShaderDescription s_colorTimesAlpha = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color * color.a; }");

        private readonly PathGeometry _geometry;
        private readonly Geometry.Resource _geometryResource;
        private readonly Pen.Resource _penResource;
        private readonly GeometryRenderNode _source;

        public ThinStrokeShaderNode()
        {
            _geometry = PathGeometry.Parse(
                "M18.25,83.6 C51.75,12.4 119.5,96.2 174.4,22.75");
            _geometryResource = _geometry.ToResource(CompositionContext.Default);
            var pen = new Pen
            {
                Thickness = { CurrentValue = 1.25f },
                Brush = { CurrentValue = new SolidColorBrush(new Color(205, 225, 85, 30)) },
                StrokeCap = { CurrentValue = StrokeCap.Round },
                StrokeJoin = { CurrentValue = StrokeJoin.Round },
            };
            _penResource = pen.ToResource(CompositionContext.Default);
            _source = new GeometryRenderNode(_geometryResource, null, _penResource);
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.RecordNode(_source, [])[0];
            context.Publish(context.Shader(source, s_colorTimesAlpha));
        }

        protected override void OnDispose(bool disposing)
        {
            _source.Dispose();
            _penResource.Dispose();
            _geometryResource.Dispose();
            base.OnDispose(disposing);
        }
    }

    private static OpaqueRenderDescription CreateCombineDescription(object structuralKey)
    {
        return OpaqueRenderDescription.Create(
            execute: static session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas =>
                {
                    foreach (RenderExecutionInput input in session.Inputs)
                        input.Draw(canvas);
                });
                session.Publish(output);
            },
            bounds: RenderOperationBoundsContract.FullInputs(
                static inputs => inputs.Aggregate(Rect.Empty, static (result, input) => result.Union(input)),
                structuralKey),
            hitTest: RenderHitTestContract.AnyInput,
            valueCardinality: RenderValueCardinality.Single,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: structuralKey,
            runtimeIdentity: new RenderRuntimeIdentity(structuralKey));
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
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

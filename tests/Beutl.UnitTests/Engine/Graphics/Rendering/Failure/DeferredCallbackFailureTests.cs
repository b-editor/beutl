using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class DeferredCallbackFailureTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [TestCase(GuardedCanvasViolation.AuthorDispose)]
    [TestCase(GuardedCanvasViolation.Snapshot)]
    [TestCase(GuardedCanvasViolation.NestedDraw)]
    [TestCase(GuardedCanvasViolation.SaveLayer)]
    [TestCase(GuardedCanvasViolation.OpacityLayer)]
    [TestCase(GuardedCanvasViolation.BlendLayer)]
    [TestCase(GuardedCanvasViolation.MaskLayer)]
    [TestCase(GuardedCanvasViolation.PaintLayer)]
    [TestCase(GuardedCanvasViolation.NativeTarget)]
    [TestCase(GuardedCanvasViolation.HiddenAllocation)]
    [TestCase(GuardedCanvasViolation.HiddenFlush)]
    public void GuardedCallbackCanvas_RejectsAuthorEscapeHatchesWithoutLeakingTargets(
        GuardedCanvasViolation violation)
    {
        using var node = new GuardedCanvasViolationNode(violation);
        var factory = new FailureTestTargetFactory();
        using var renderer = FailureTestSupport.CreateRenderer(node, factory, useRenderCache: false);

        Assert.That(() => renderer.Rasterize(), Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(factory.Targets, Has.All.Matches<FailureTestRenderTarget>(target => !target.IsDisposed));
        });
    }

    [TestCase(GeometryFailure.UndeclaredInputReadback)]
    [TestCase(GeometryFailure.DuplicateInputReadback)]
    [TestCase(GeometryFailure.Callback)]
    [TestCase(GeometryFailure.InvalidShrink)]
    [TestCase(GeometryFailure.SecondCanvasOpen)]
    [TestCase(GeometryFailure.UseAfterCanvasClose)]
    public void GeometryDeferredPhases_PreserveTheFirstFailureAndInvalidateTheSession(
        GeometryFailure failurePoint)
    {
        using var node = new GeometryFailureNode(failurePoint);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        Exception? failure = Assert.Catch(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.RetainedSession, Is.Not.Null);
            Assert.That(() => _ = node.RetainedSession!.OutputBounds, Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void GeometryOutputAcquisitionFailure_HappensBeforeCallbackAndReturnsPriorTargets()
    {
        using var node = new GeometryFailureNode(GeometryFailure.Callback);
        var factory = new FailureTestTargetFactory(failAt: 2);
        using var renderer = FailureTestSupport.CreateRenderer(node, factory, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("could not allocate"));
            Assert.That(node.CallbackEntries, Is.Zero);
            Assert.That(factory.CreateCalls, Is.EqualTo(3));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void CallbackCanvasOpenFailure_PreservesProviderExceptionAndKeepsTheFacadeOneShot()
    {
        var primary = new InvalidOperationException("callback-canvas-open-primary");
        var token = new RenderExecutionSessionToken();
        var canvas = new RenderCallbackCanvas(
            token,
            density: 1,
            s_bounds,
            () => throw primary,
            CallbackCanvasCapability.Draw);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => canvas.Use(static _ => { }));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(() => canvas.Use(static _ => { }), Throws.TypeOf<InvalidOperationException>());
        });
        token.Complete();
        Assert.That(() => _ = canvas.LogicalBounds, Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(OpaqueTopology.Source)]
    [TestCase(OpaqueTopology.Map)]
    [TestCase(OpaqueTopology.Combine)]
    [TestCase(OpaqueTopology.Expand)]
    public void EveryOpaqueTopology_PropagatesItsDeferredCallbackFailure(OpaqueTopology topology)
    {
        var primary = new InvalidOperationException($"opaque-{topology}");
        using var node = new OpaqueTopologyFailureNode(topology, primary);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(node.FaultingCallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [TestCase(DynamicOutputFailure.MissingRequiredOutput)]
    [TestCase(DynamicOutputFailure.ExceedsMaximum)]
    [TestCase(DynamicOutputFailure.OutOfDeclaredBounds)]
    public void OpaqueDynamicOutputValidation_RejectsInvalidPublicationAtomically(
        DynamicOutputFailure failurePoint)
    {
        using var node = new DynamicOutputFailureNode(failurePoint);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        Exception? failure = Assert.Catch(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void UndeclaredResourceUse_IsRejectedInsideTheRealOpaqueSession()
    {
        using var node = new UndeclaredResourceNode();
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("was not declared"));
            Assert.That(node.Borrowed.DisposeCalls, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void SuccessfulDeferredCallback_SealsSessionInputOutputAndCanvasFacadesAfterReturn()
    {
        using var node = new RetainedFacadeNode();
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(() => _ = node.Session!.OutputBounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = node.Input!.Bounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = node.Output!.Bounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = node.CanvasFacade!.LogicalBounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => node.ImmediateCanvas!.Clear(), Throws.Exception);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [TestCase(HiddenRendererCall.Render, false)]
    [TestCase(HiddenRendererCall.Rasterize, false)]
    [TestCase(HiddenRendererCall.Measure, false)]
    [TestCase(HiddenRendererCall.HitTest, false)]
    [TestCase(HiddenRendererCall.Render, true)]
    [TestCase(HiddenRendererCall.Rasterize, true)]
    [TestCase(HiddenRendererCall.Measure, true)]
    [TestCase(HiddenRendererCall.HitTest, true)]
    public void DeferredCallback_RejectsHiddenRendererLaunchAndClearsTheGuard(
        HiddenRendererCall call,
        bool constructBeforeCallback)
    {
        using var hiddenRoot = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var preconstructed = constructBeforeCallback ? CreateHiddenRenderer(hiddenRoot) : null;
        using var node = new HiddenRendererLaunchNode(hiddenRoot, preconstructed, call);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("cannot be launched"));
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });

        using RenderNodeRenderer outside = preconstructed ?? CreateHiddenRenderer(hiddenRoot);
        Assert.That(() => outside.Measure(), Throws.Nothing,
            "The execution-callback guard must be cleared even when the callback fails.");
    }

    private static RenderNodeRenderer CreateHiddenRenderer(RenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
            });

    public enum GuardedCanvasViolation
    {
        AuthorDispose,
        Snapshot,
        NestedDraw,
        SaveLayer,
        OpacityLayer,
        BlendLayer,
        MaskLayer,
        PaintLayer,
        NativeTarget,
        HiddenAllocation,
        HiddenFlush,
    }

    public enum GeometryFailure
    {
        UndeclaredInputReadback,
        DuplicateInputReadback,
        Callback,
        InvalidShrink,
        SecondCanvasOpen,
        UseAfterCanvasClose,
    }

    public enum OpaqueTopology
    {
        Source,
        Map,
        Combine,
        Expand,
    }

    public enum DynamicOutputFailure
    {
        MissingRequiredOutput,
        ExceedsMaximum,
        OutOfDeclaredBounds,
    }

    public enum HiddenRendererCall
    {
        Render,
        Rasterize,
        Measure,
        HitTest,
    }

    private sealed class HiddenRendererLaunchNode(
        RenderNode hiddenRoot,
        RenderNodeRenderer? preconstructed,
        HiddenRendererCall call) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                ExecuteDeferred,
                structuralKey: $"hidden-renderer-{call}-{preconstructed is not null}")));
        }

        private void ExecuteDeferred(OpaqueRenderSession session)
        {
            CallbackEntries++;
            using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
            output.Canvas.Use(canvas =>
            {
                RenderNodeRenderer renderer = preconstructed ?? CreateHiddenRenderer(hiddenRoot);
                try
                {
                    switch (call)
                    {
                        case HiddenRendererCall.Render:
                            renderer.Render(canvas);
                            break;
                        case HiddenRendererCall.Rasterize:
                            using (renderer.Rasterize())
                            {
                            }
                            break;
                        case HiddenRendererCall.Measure:
                            _ = renderer.Measure();
                            break;
                        case HiddenRendererCall.HitTest:
                            _ = renderer.HitTest(new Point(1, 1));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                finally
                {
                    if (preconstructed is null)
                        renderer.Dispose();
                }
            });
            session.Publish(output);
        }
    }

    private sealed class GuardedCanvasViolationNode(GuardedCanvasViolation violation) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = FailureTestSupport.SourceDescription(
                session =>
                {
                    CallbackEntries++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    using SKSurface hiddenSurface = SKSurface.Create(new SKImageInfo(1, 1));
                    output.Canvas.Use(canvas => InvokeViolation(canvas, hiddenSurface));
                    session.Publish(output);
                },
                structuralKey: $"guarded-canvas-{violation}");
            context.Publish(context.OpaqueSource(description));
        }

        private void InvokeViolation(ImmediateCanvas canvas, SKSurface hiddenSurface)
        {
            switch (violation)
            {
                case GuardedCanvasViolation.AuthorDispose:
                    canvas.Dispose();
                    break;
                case GuardedCanvasViolation.Snapshot:
                    _ = canvas.Snapshot();
                    break;
                case GuardedCanvasViolation.NestedDraw:
                    canvas.DrawNode(this);
                    break;
                case GuardedCanvasViolation.SaveLayer:
                    canvas.PushLayer().Dispose();
                    break;
                case GuardedCanvasViolation.OpacityLayer:
                    canvas.PushOpacity(0.5f).Dispose();
                    break;
                case GuardedCanvasViolation.BlendLayer:
                    canvas.PushBlendMode(BlendMode.Multiply).Dispose();
                    break;
                case GuardedCanvasViolation.MaskLayer:
                    canvas.PushOpacityMask(Brushes.Resource.White, s_bounds).Dispose();
                    break;
                case GuardedCanvasViolation.PaintLayer:
                    using (var paint = new SKPaint())
                        canvas.PushPaint(paint).Dispose();
                    break;
                case GuardedCanvasViolation.NativeTarget:
                    using (RenderTarget.GetRenderTarget(canvas))
                    {
                    }
                    break;
                case GuardedCanvasViolation.HiddenAllocation:
                    using (canvas.CreateExecutionView())
                    {
                    }
                    break;
                case GuardedCanvasViolation.HiddenFlush:
                    canvas.DrawSurface(hiddenSurface, default);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private sealed class GeometryFailureNode(GeometryFailure failurePoint) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public GeometrySession? RetainedSession { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: $"geometry-source-{failurePoint}"));
            bool readback = failurePoint == GeometryFailure.DuplicateInputReadback;
            GeometryDescription description = GeometryDescription.Create(
                session =>
                {
                    CallbackEntries++;
                    RetainedSession = session;
                    switch (failurePoint)
                    {
                        case GeometryFailure.UndeclaredInputReadback:
                            session.Input.UseSnapshot(static _ => { });
                            break;
                        case GeometryFailure.DuplicateInputReadback:
                            session.Input.UseSnapshot(static _ => { });
                            session.Input.UseSnapshot(static _ => { });
                            break;
                        case GeometryFailure.Callback:
                            throw new InvalidOperationException("geometry-callback-primary");
                        case GeometryFailure.InvalidShrink:
                            session.SetOutputBounds(new Rect(-1, -1, 12, 12));
                            break;
                        case GeometryFailure.SecondCanvasOpen:
                            session.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                            session.Canvas.Use(static canvas => canvas.Clear(Colors.Blue));
                            break;
                        case GeometryFailure.UseAfterCanvasClose:
                            {
                                ImmediateCanvas? retained = null;
                                session.Canvas.Use(canvas => retained = canvas);
                                retained!.Clear();
                                break;
                            }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: $"geometry-failure-{failurePoint}",
                requiresReadback: readback);
            context.Publish(context.Geometry(source, description));
        }
    }

    private sealed class OpaqueTopologyFailureNode(
        OpaqueTopology topology,
        InvalidOperationException failure) : RenderNode
    {
        public int FaultingCallbackEntries { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            void Fail(OpaqueRenderSession _)
            {
                FaultingCallbackEntries++;
                throw failure;
            }

            if (topology == OpaqueTopology.Source)
            {
                context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                    Fail,
                    RenderOperationBoundsContract.Source(s_bounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    structuralKey: "faulting-opaque-source")));
                return;
            }

            RenderFragmentHandle first = context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: $"opaque-{topology}-input-a"));
            if (topology == OpaqueTopology.Map)
            {
                OpaqueRenderDescription map = OpaqueRenderDescription.Create(
                    Fail,
                    RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderHitTestContract.AnyInput,
                    RenderValueCardinality.Single,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "faulting-opaque-map");
                context.Publish(context.OpaqueMap(first, map));
                return;
            }

            RenderFragmentHandle second = context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: $"opaque-{topology}-input-b"));
            OpaqueRenderDescription many = OpaqueRenderDescription.Create(
                Fail,
                RenderOperationBoundsContract.FullInputs(
                    static bounds => bounds.Aggregate(default(Rect), static (result, value) => result.Union(value)),
                    $"opaque-{topology}-bounds"),
                RenderHitTestContract.AnyInput,
                topology == OpaqueTopology.Combine ? RenderValueCardinality.Single : RenderValueCardinality.Dynamic,
                RenderScaleContract.Vector,
                structuralKey: $"faulting-opaque-{topology}");
            RenderFragmentHandle output = topology == OpaqueTopology.Combine
                ? context.OpaqueCombine([first, second], many)
                : context.OpaqueExpand([first, second], many);
            context.Publish(output);
        }
    }

    private sealed class DynamicOutputFailureNode(DynamicOutputFailure failurePoint) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    switch (failurePoint)
                    {
                        case DynamicOutputFailure.MissingRequiredOutput:
                            return;
                        case DynamicOutputFailure.ExceedsMaximum:
                            {
                                using OpaqueRenderOutput first = session.CreateOutput(new Rect(0, 0, 4, 8));
                                using OpaqueRenderOutput second = session.CreateOutput(new Rect(4, 0, 4, 8));
                                session.Publish(first);
                                session.Publish(second);
                                return;
                            }
                        case DynamicOutputFailure.OutOfDeclaredBounds:
                            using (session.CreateOutput(new Rect(-1, 0, 9, 8)))
                            {
                            }
                            return;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                },
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                failurePoint == DynamicOutputFailure.ExceedsMaximum
                    ? RenderValueCardinality.Range(0, 1)
                    : RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: $"dynamic-output-{failurePoint}");
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class UndeclaredResourceNode : RenderNode
    {
        public FailureTestDisposable Borrowed { get; } = new();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<FailureTestDisposable> borrowed = context.Borrow(
                Borrowed,
                "undeclared-callback-resource",
                0);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session => session.UseResource(borrowed, static _ => { }),
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "undeclared-resource-source");
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class RetainedFacadeNode : RenderNode
    {
        public OpaqueRenderSession? Session { get; private set; }

        public RenderExecutionInput? Input { get; private set; }

        public OpaqueRenderOutput? Output { get; private set; }

        public RenderCallbackCanvas? CanvasFacade { get; private set; }

        public ImmediateCanvas? ImmediateCanvas { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: "retained-facade-source"));
            OpaqueRenderDescription map = OpaqueRenderDescription.Create(
                session =>
                {
                    Session = session;
                    Input = session.Inputs.Single();
                    OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    Output = output;
                    CanvasFacade = output.Canvas;
                    output.Canvas.Use(canvas =>
                    {
                        ImmediateCanvas = canvas;
                        session.Inputs.Single().Draw(canvas);
                    });
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: "retained-facade-map");
            context.Publish(context.OpaqueMap(source, map));
        }
    }
}

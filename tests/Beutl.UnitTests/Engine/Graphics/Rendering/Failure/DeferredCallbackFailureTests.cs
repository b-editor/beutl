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
    [TestCase(GuardedCanvasViolation.OpacityLayer)]
    [TestCase(GuardedCanvasViolation.BlendLayer)]
    [TestCase(GuardedCanvasViolation.MaskLayer)]
    [TestCase(GuardedCanvasViolation.PaintLayer)]
    [TestCase(GuardedCanvasViolation.NativeTarget)]
    [TestCase(GuardedCanvasViolation.HiddenAllocation)]
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

    [Test]
    public void CallbackCanvasOpenFailure_PreservesProviderExceptionAndKeepsTheFacadeOneShot()
    {
        var primary = new InvalidOperationException("callback-canvas-open-primary");
        var token = new RenderExecutionSessionToken();
        var canvas = new RenderCallbackCanvas(
            token,
            density: 1,
            s_bounds,
            PixelRect.FromRect(s_bounds, 1),
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

    [Test]
    public void SessionCompletion_PreservesBodyFailureWhenAnActiveCanvasAlsoFailsCompletion()
    {
        var primary = new InvalidOperationException("session-body-primary");
        var token = new RenderExecutionSessionToken();
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, logicalSize: s_bounds.Size);
        token.EnterCanvas(canvas, facade: null);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => token.RunAndComplete(() => throw primary));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(token.ThrowIfInactive, Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void SessionCompletion_SurfacesActiveCanvasFailureWithoutAPrimaryFailure()
    {
        var token = new RenderExecutionSessionToken();
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, logicalSize: s_bounds.Size);
        token.EnterCanvas(canvas, facade: null);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => token.RunAndComplete(static () => { }));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("canvas is still active"));
            Assert.That(token.ThrowIfInactive, Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void AnOpaqueSource_PropagatesItsDeferredCallbackFailure()
    {
        var primary = new InvalidOperationException("opaque-source");
        using var node = new OpaqueSourceFailureNode(primary);
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

        Type expectedType = failurePoint == DynamicOutputFailure.OutOfDeclaredBounds
            ? typeof(ArgumentException)
            : typeof(InvalidOperationException);
        string expectedMessage = failurePoint switch
        {
            DynamicOutputFailure.MissingRequiredOutput => "published 0 values outside its declared cardinality [1, 1]",
            DynamicOutputFailure.ExceedsMaximum => "published 2 values outside its declared cardinality [0, 1]",
            DynamicOutputFailure.OutOfDeclaredBounds => "contained by the declared output bounds",
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null),
        };

        Assert.Multiple(() =>
        {
            Assert.That(failure!.GetType(), Is.EqualTo(expectedType));
            Assert.That(failure!.Message, Does.Contain(expectedMessage));
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(node.ReachedFailurePoint, Is.EqualTo(failurePoint));
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
    public void SuccessfulDeferredCallback_SealsSessionOutputAndCanvasFacadesAfterReturn()
    {
        using var node = new RetainedFacadeNode();
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(() => _ = node.Session!.OutputBounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = node.Output!.Bounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = node.CanvasFacade!.LogicalBounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(node.ImmediateCanvas, Is.Not.Null);
            Assert.That(
                () => node.ImmediateCanvas!.Clear(),
                Throws.TypeOf<ObjectDisposedException>());
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
        using var preconstructed = constructBeforeCallback ? FailureTestSupport.CreateRenderer(hiddenRoot) : null;
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

        using RenderNodeRenderer outside = preconstructed ?? FailureTestSupport.CreateRenderer(hiddenRoot);
        Assert.That(() => outside.Measure(), Throws.Nothing,
            "The execution-callback guard must be cleared even when the callback fails.");
    }

    public enum GuardedCanvasViolation
    {
        AuthorDispose,
        OpacityLayer,
        BlendLayer,
        MaskLayer,
        PaintLayer,
        NativeTarget,
        HiddenAllocation,
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
                ExecuteDeferred)));
        }

        private void ExecuteDeferred(OpaqueRenderSession session)
        {
            CallbackEntries++;
            using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
            output.Canvas.Use(canvas =>
            {
                RenderNodeRenderer renderer = preconstructed ?? FailureTestSupport.CreateRenderer(hiddenRoot);
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
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    CallbackEntries++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(InvokeViolation);
                    session.Publish(output);
                })));
        }


        private void InvokeViolation(ImmediateCanvas canvas)
        {
            switch (violation)
            {
                case GuardedCanvasViolation.AuthorDispose:
                    canvas.Dispose();
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
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private sealed class OpaqueSourceFailureNode(InvalidOperationException failure) : RenderNode
    {
        public int FaultingCallbackEntries { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            void Fail(OpaqueRenderSession _)
            {
                FaultingCallbackEntries++;
                throw failure;
            }

            context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                Fail,
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
        }
    }

    private sealed class DynamicOutputFailureNode(DynamicOutputFailure failurePoint) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public DynamicOutputFailure? ReachedFailurePoint { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    CallbackEntries++;
                    ReachedFailurePoint = failurePoint;
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
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                failurePoint == DynamicOutputFailure.ExceedsMaximum
                    ? RenderValueCardinality.Range(0, 1)
                    : RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class UndeclaredResourceNode : RenderNode
    {
        public FailureTestDisposable Borrowed { get; } = new();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<FailureTestDisposable> borrowed = context.Borrow(
                Borrowed);
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                session => session.UseResource(borrowed, static _ => { }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class RetainedFacadeNode : RenderNode
    {
        public OpaqueRenderSession? Session { get; private set; }

        public OpaqueRenderOutput? Output { get; private set; }

        public RenderCallbackCanvas? CanvasFacade { get; private set; }

        public ImmediateCanvas? ImmediateCanvas { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription source = OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    Session = session;
                    OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    Output = output;
                    CanvasFacade = output.Canvas;
                    output.Canvas.Use(canvas =>
                    {
                        ImmediateCanvas = canvas;
                        canvas.Clear(Colors.White);
                    });
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(source));
        }
    }
}

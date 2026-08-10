using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

/// <summary>
/// Covers stable named addressing for every session that exposes declared resources.
/// </summary>
[TestFixture]
public sealed class DeclaredResourceAddressingTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [TestCase(DeclaredResourceSession.Opaque)]
    [TestCase(DeclaredResourceSession.Geometry)]
    [TestCase(DeclaredResourceSession.TargetScope)]
    [TestCase(DeclaredResourceSession.TargetCommand)]
    public void ANameOutsideTheDeclaredListThrowsKeyNotFound(DeclaredResourceSession session)
    {
        using var node = new DeclaredResourceNode(session, DeclaredResourceUse.MissingName);
        using RenderNodeRenderer renderer = FailureTestSupport.CreateRenderer(node);

        KeyNotFoundException? failure =
            Assert.Throws<KeyNotFoundException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("missing"));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [TestCase(DeclaredResourceSession.Opaque)]
    [TestCase(DeclaredResourceSession.Geometry)]
    [TestCase(DeclaredResourceSession.TargetScope)]
    [TestCase(DeclaredResourceSession.TargetCommand)]
    public void AMismatchedTypeArgumentThrowsInvalidOperation(DeclaredResourceSession session)
    {
        using var node = new DeclaredResourceNode(session, DeclaredResourceUse.TypeMismatch);
        using RenderNodeRenderer renderer = FailureTestSupport.CreateRenderer(node);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        TestContext.Out.WriteLine(failure!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(failure.Message, Does.Contain("Declared resource 'first'"));
            Assert.That(failure.Message, Does.Contain("RenderResource<Geometry.Resource>"),
                "the message must name the type the callback asked for");
            Assert.That(failure.Message, Does.Contain("RenderResource<SolidColorBrush.Resource>"),
                "and the type actually declared under that name");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void ReorderingResourcesOfTheSameTypePreservesTheirNamedMeaning()
    {
        using var declared = new DeclaredResourceNode(
            DeclaredResourceSession.Opaque,
            DeclaredResourceUse.FirstBrush);
        using var reordered = new DeclaredResourceNode(
            DeclaredResourceSession.Opaque,
            DeclaredResourceUse.FirstBrush)
        { SwapDeclaredBrushes = true };

        Assert.Multiple(() =>
        {
            Assert.That(Render(declared), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)));
            Assert.That(Render(reordered), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)),
                "the stable name must keep addressing the first brush after declaration order changes");
        });
    }

    private static ulong Render(RenderNode node)
    {
        using RenderNodeRenderer renderer = FailureTestSupport.CreateRenderer(node);
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Assert.That(rasterization.IsEmpty, Is.False);
        return ReadFirstPixel(rasterization.Bitmap!);
    }

    private static ulong ToPremultipliedHalfBits(Color color)
    {
        using var target = new FailureTestRenderTarget(new PixelSize(1, 1));
        target.Value.Canvas.Clear(color.ToSKColor());
        using Bitmap snapshot = target.Snapshot();
        return ReadFirstPixel(snapshot);
    }

    private static ulong ReadFirstPixel(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return ((ulong)pixels[0] << 48)
               | ((ulong)pixels[1] << 32)
               | ((ulong)pixels[2] << 16)
               | pixels[3];
    }

    public enum DeclaredResourceSession
    {
        Opaque,
        Geometry,
        TargetScope,
        TargetCommand,
    }

    public enum DeclaredResourceUse
    {
        MissingName,
        TypeMismatch,
        FirstBrush,
    }

    private sealed class DeclaredResourceNode(
        DeclaredResourceSession session,
        DeclaredResourceUse use) : RenderNode
    {
        public bool SwapDeclaredBrushes { get; init; }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<SolidColorBrush.Resource> first = Borrow(context, Brushes.Resource.Red);
            RenderResource<SolidColorBrush.Resource> second = Borrow(context, Brushes.Resource.Lime);
            RenderResourceBinding[] declared = SwapDeclaredBrushes
                ? [second.Bind("second"), first.Bind("first")]
                : [first.Bind("first"), second.Bind("second")];

            bool failInSource = session == DeclaredResourceSession.Opaque
                                && use != DeclaredResourceUse.FirstBrush;
            RenderFragmentHandle source = context.OpaqueSource(
                OpaqueRenderDescription.Create(
                    failInSource ? use : DeclaredResourceUse.FirstBrush,
                    static (opaque, state) =>
                    {
                        if (state == DeclaredResourceUse.MissingName)
                        {
                            opaque.UseDeclaredResource<SolidColorBrush.Resource>("missing", static _ => { });
                            return;
                        }

                        if (state == DeclaredResourceUse.TypeMismatch)
                        {
                            opaque.UseDeclaredResource<Geometry.Resource>("first", static _ => { });
                            return;
                        }

                        opaque.UseDeclaredResource<SolidColorBrush.Resource>("first", brush =>
                        {
                            using OpaqueRenderOutput output = opaque.CreateOutput(s_bounds);
                            output.Canvas.Use(canvas => canvas.DrawRectangle(s_bounds, brush, null));
                            opaque.Publish(output);
                        });
                    },
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    resources: declared));

            switch (session)
            {
                case DeclaredResourceSession.Geometry:
                    context.Publish(context.ContributeValues(context.Geometry(
                        source,
                        GeometryDescription.Create(
                            use,
                            static (geometry, state) =>
                            {
                                if (state == DeclaredResourceUse.MissingName)
                                    geometry.UseDeclaredResource<SolidColorBrush.Resource>("missing", static _ => { });
                                else
                                    geometry.UseDeclaredResource<Geometry.Resource>("first", static _ => { });
                            },
                            RenderBoundsContract.Identity,
                            RenderHitTestContract.AnyInput,
                            resources: declared))));
                    break;

                case DeclaredResourceSession.TargetScope:
                    context.Publish(context.Layer(
                        [
                            context.TargetScope(
                                source,
                                TargetScopeDescription.Create(
                                    use,
                                    static (scope, state) =>
                                    {
                                        if (state == DeclaredResourceUse.MissingName)
                                            scope.UseDeclaredResource<SolidColorBrush.Resource>("missing", static _ => { });
                                        else
                                            scope.UseDeclaredResource<Geometry.Resource>("first", static _ => { });
                                    },
                                    RenderBoundsContract.Identity,
                                    RenderHitTestContract.AnyInput,
                                    RenderScaleContract.PreserveInputSupply,
                                    deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
                                    resources: declared)),
                        ],
                        s_bounds));
                    break;

                case DeclaredResourceSession.TargetCommand:
                    context.Publish(context.ContributeValues(source));
                    context.Publish(context.TargetCommand(
                        [],
                        TargetCommandDescription.Create(
                            use,
                            static (command, state) =>
                            {
                                if (state == DeclaredResourceUse.MissingName)
                                    command.UseDeclaredResource<SolidColorBrush.Resource>("missing", static _ => { });
                                else
                                    command.UseDeclaredResource<Geometry.Resource>("first", static _ => { });
                            },
                            TargetRegion.Empty,
                            Rect.Empty,
                            RenderHitTestContract.None,
                            resources: declared)));
                    break;

                default:
                    context.Publish(context.ContributeValues(source));
                    break;
            }
        }

        private static RenderResource<SolidColorBrush.Resource> Borrow(
            RenderNodeContext context,
            SolidColorBrush.Resource brush)
            => context.Borrow(brush, brush.GetOriginal().Id, brush.Version);
    }
}

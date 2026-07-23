using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class BackdropOrderingTests
{
    private static readonly Rect s_domain = new(0, 0, 160, 90);
    private static readonly Rect s_drawBounds = new(12, 8, 80, 48);

    [TestCase(BackdropScope.Root)]
    [TestCase(BackdropScope.Blend)]
    [TestCase(BackdropScope.Transform)]
    [TestCase(BackdropScope.Filter)]
    public void SnapshotClearDraw_PreservesOneCaptureAndItsTargetTokenOrder(BackdropScope scope)
    {
        using ContainerRenderNode root = CreateTree(scope);
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_domain,
            owner: owner);
        var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);

        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = graph.Fragments
            .ToDictionary(
                static fragment => fragment.Id,
                static fragment => (RenderFragmentReference)fragment.Payload!);
        TargetDependencyStep[] steps = compiled.TargetDependencies.Steps.ToArray();

        RenderFragmentReference[] captures = references.Values
            .Where(static reference => reference.Kind == RenderFragmentKind.BuiltInBackdropCapture)
            .ToArray();
        Assert.That(captures, Has.Length.EqualTo(1),
            "SnapshotBackdrop must create one request-local target capture.");

        RenderFragmentReference capture = captures[0];
        TargetDependencyStep captureStep = steps.Single(step =>
            references[step.FragmentId].Kind == RenderFragmentKind.BuiltInBackdropCapture);
        TargetDependencyStep[] commandSteps = steps.Where(step =>
            references[step.FragmentId].Kind == RenderFragmentKind.TargetCommand).ToArray();
        TargetDependencyStep drawStep = commandSteps.Single(step =>
            step.TargetReadValueId == captureStep.TargetReadValueId);
        TargetDependencyStep clearStep = commandSteps.Single(step =>
            step.TargetReadValueId is null);
        RenderFragmentReference draw = references[drawStep.FragmentId];

        int captureIndex = Array.IndexOf(steps, captureStep);
        int clearIndex = Array.IndexOf(steps, clearStep);
        int drawIndex = Array.IndexOf(steps, drawStep);
        int captureUses = draw.Inputs.Count(input => ReferenceEquals(input, capture));
        int implicitContributions = references.Values.Count(reference =>
            reference.Kind == RenderFragmentKind.ContributeValues
            && reference.Inputs.Any(input => ReferenceEquals(input, capture)));

        Assert.Multiple(() =>
        {
            Assert.That(captureStep.TargetReadValueId, Is.Not.Null,
                "The capture must name the request-owned value read by DrawBackdrop.");
            Assert.That(capture.ContributesValuesToTarget, Is.False,
                "A target capture anchors pixels but must not redraw them implicitly.");
            Assert.That(implicitContributions, Is.Zero);
            Assert.That(captureIndex, Is.LessThan(clearIndex));
            Assert.That(clearIndex, Is.LessThan(drawIndex));
            Assert.That(captureUses, Is.EqualTo(1),
                "DrawBackdrop must consume exactly the value produced by this capture.");
            Assert.That(drawStep.TargetReadValueId, Is.EqualTo(captureStep.TargetReadValueId));
            Assert.That(clearStep.ScopeId, Is.EqualTo(captureStep.ScopeId));
        });

        if (scope == BackdropScope.Root)
        {
            Assert.Multiple(() =>
            {
                Assert.That(drawStep.ScopeId, Is.EqualTo(captureStep.ScopeId));
                Assert.That(clearStep.InputToken, Is.EqualTo(captureStep.OutputToken));
                Assert.That(drawStep.InputToken, Is.EqualTo(clearStep.OutputToken));
            });
        }
        else
        {
            Assert.That(drawStep.ScopeId, Is.Not.EqualTo(captureStep.ScopeId),
                "The nested draw must consume the capture from inside its own authored scope.");

            if (scope == BackdropScope.Filter)
            {
                RenderFragmentReference isolation = references.Values
                    .Single(static reference => reference.Kind == RenderFragmentKind.Layer);
                var payload = (LayerRenderFragmentPayload)isolation.Payload!;
                Assert.Multiple(() =>
                {
                    Assert.That(payload.Domain, Is.EqualTo(s_drawBounds),
                        "A finite target write must use its affected region rather than a symbolic capture hint.");
                    Assert.That(isolation.BoundsRequirement,
                        Is.EqualTo(RenderFragmentBoundsRequirement.Finite));
                    Assert.That(references.Values.Any(static reference =>
                        reference.Kind == RenderFragmentKind.LegacyFilterEffect), Is.True,
                        "The filter must remain present after target-dependent input isolation.");
                });
            }
        }
    }

    [Test]
    public void SnapshotDraw_WithoutCurrentCapture_UsesPersistedFallbackPath()
    {
        using var snapshot = new SnapshotBackdropRenderNode();
        var fallback = new Bitmap((int)s_domain.Width, (int)s_domain.Height);
        fallback.GetPixelSpan().Fill(byte.MaxValue);
        ((IBuiltInBackdropCaptureSink)snapshot).CommitBackdropCapture(fallback, density: 1f);
        using var draw = new DrawBackdropRenderNode(snapshot, s_drawBounds);
        using var renderer = new RenderNodeRenderer(
            draw,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_domain,
                UseRenderCache = false,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds.Contains(s_drawBounds), Is.True,
                "The raw fallback may conservatively retain the full target domain.");
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(rasterization.Bitmap!.GetPixelSpan().ToArray(), Has.Some.Not.Zero,
                "The unbound draw must use the persisted snapshot rather than a transparent no-op.");
        });
    }

    private static ContainerRenderNode CreateTree(BackdropScope scope)
    {
        var root = new ContainerRenderNode();
        var snapshot = new SnapshotBackdropRenderNode();
        var clear = new ClearRenderNode(Colors.Transparent);
        var draw = new DrawBackdropRenderNode(snapshot, s_drawBounds);

        root.AddChild(snapshot);
        root.AddChild(clear);
        root.AddChild(WrapDraw(draw, scope));
        return root;
    }

    private static RenderNode WrapDraw(DrawBackdropRenderNode draw, BackdropScope scope)
    {
        ContainerRenderNode? wrapper = scope switch
        {
            BackdropScope.Root => null,
            BackdropScope.Blend => new BlendModeRenderNode(BlendMode.Multiply),
            BackdropScope.Transform => new TransformRenderNode(
                Matrix.CreateTranslation(7, 11),
                TransformOperator.Prepend),
            BackdropScope.Filter => CreateFilterScope(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };

        if (wrapper is null)
            return draw;

        wrapper.AddChild(draw);
        return wrapper;
    }

    private static FilterEffectRenderNode CreateFilterScope()
    {
        var effect = new MosaicEffect();
        effect.TileSize.CurrentValue = new Size(8, 8);
        return new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
    }

    public enum BackdropScope
    {
        Root,
        Blend,
        Transform,
        Filter,
    }
}

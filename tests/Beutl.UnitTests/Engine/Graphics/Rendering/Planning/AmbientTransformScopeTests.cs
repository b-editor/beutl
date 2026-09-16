using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

/// <summary>
/// Pins that a scope whose transform is defined against the ambient transform - what
/// <see cref="TransformOperator.Append"/> and <see cref="TransformOperator.Set"/> declare - is measured, hit
/// tested and committed in the space it actually draws in.
/// </summary>
/// <remarks>
/// Each case states the composition an <see cref="ImmediateCanvas"/> would build for the same pushes and
/// requires every answer to agree with it: the conservative output bounds, the pixels the request commits, and
/// the point the graph reports a hit at.
/// </remarks>
[TestFixture]
public sealed class AmbientTransformScopeTests
{
    private static readonly Rect s_domain = new(0, 0, 64, 64);
    private static readonly Rect s_mark = new(8, 8, 8, 8);
    private static readonly Color s_markColor = Color.FromArgb(255, 255, 0, 0);

    private static readonly Matrix s_translate = Matrix.CreateTranslation(20, 0);
    private static readonly Matrix s_scale = Matrix.CreateScale(2, 2);
    private static readonly Matrix s_shift = Matrix.CreateTranslation(10, 0);

    public static IEnumerable<TestCaseData> Compositions()
    {
        // Every case keeps the drawn rectangle on whole pixels inside the domain, so the pixels a request
        // commits can be compared to the bounds it selected without a resampling fringe in the way.
        yield return Case("set under a translation",
            (s_translate, TransformOperator.Prepend),
            (Matrix.Identity, TransformOperator.Set));
        yield return Case("set under a scale",
            (s_scale, TransformOperator.Prepend),
            (s_shift, TransformOperator.Set));
        yield return Case("append under a scale",
            (s_scale, TransformOperator.Prepend),
            (s_shift, TransformOperator.Append));
        yield return Case("append of a scale under a translation",
            (Matrix.CreateTranslation(4, 4), TransformOperator.Prepend),
            (s_scale, TransformOperator.Append));
        yield return Case("prepend under an append",
            (s_scale, TransformOperator.Append),
            (s_shift, TransformOperator.Prepend));
        yield return Case("set under an append under a prepend",
            (s_translate, TransformOperator.Prepend),
            (s_scale, TransformOperator.Append),
            (s_shift, TransformOperator.Set));
        yield return Case("append under a set under a scale",
            (s_scale, TransformOperator.Prepend),
            (s_translate, TransformOperator.Set),
            (s_shift, TransformOperator.Append));
        yield return Case("three prepends",
            (Matrix.CreateTranslation(4, 0), TransformOperator.Prepend),
            (s_scale, TransformOperator.Prepend),
            (Matrix.CreateTranslation(6, 0), TransformOperator.Prepend));

        static TestCaseData Case(string name, params (Matrix Matrix, TransformOperator Operator)[] pushes)
            => new TestCaseData((object)pushes).SetName($"{{m}}({name})");
    }

    [TestCaseSource(nameof(Compositions))]
    public void ACompositionIsMeasuredAndHitTestedWhereTheCanvasDrawsIt(
        (Matrix Matrix, TransformOperator Operator)[] pushes)
    {
        Matrix composed = Compose(pushes);
        Rect expected = s_mark.TransformToAABB(composed);

        using RenderNode root = BuildChain(pushes, new MarkNode());
        using var renderer = CreateRenderer(root);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(expected),
                "the conservative output bounds must describe where the composition draws");
            Assert.That(rasterization.Bounds, Is.EqualTo(expected));
            Assert.That(PaintedBounds(rasterization), Is.EqualTo(expected),
                "the committed pixels must fill the bounds the request selected");
            Assert.That(renderer.HitTest(s_mark.Center * composed), Is.True);
            Assert.That(renderer.HitTest(Outside(expected)), Is.False);
        });
    }

    [Test]
    public void ASetResolvesAgainstATransformMeasuredBelowIt()
    {
        // A DrawableGroup's alignment transform derives its matrix from content measured under it, so nothing
        // recorded above it can predict the ambient a Set inside it has to discard.
        using DrawableGroup.CustomTransformRenderNode group = new(
            new TranslateTransform(20, 0).ToResource(CompositionContext.Default),
            default,
            s_domain.Size,
            AlignmentX.Left,
            AlignmentY.Top,
            new MemoryNode<Rect>(s_domain));
        group.AddChild(BuildChain([(Matrix.Identity, TransformOperator.Set)], new MarkNode()));
        using var renderer = CreateRenderer(group);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_mark));
            Assert.That(PaintedBounds(rasterization), Is.EqualTo(s_mark));
            Assert.That(renderer.HitTest(s_mark.Center), Is.True);
        });
    }

    /// <remarks>
    /// A known limit, pinned so it is met where it applies. A composition reaches its destination by composing
    /// with what its ancestors contribute, so <see cref="TransformOperator.Set"/> is the input-space matrix
    /// <c>M * ambient⁻¹</c> - and a singular ambient has none, because every product with a singular matrix is
    /// singular. The ancestor measures the subtree as empty on its own account too: with no inverse it declares
    /// a full-input contract whose forward mapping collapses whatever its input reports. Escaping it means
    /// detaching the subtree from its ancestors' bounds composition, which is tracked by #2422; measured
    /// identical on f6596ea05, before any of this was resolved.
    /// </remarks>
    [Test]
    public void ASetUnderASingularAmbientStaysCollapsed()
    {
        using RenderNode root = BuildChain(
            [
                (Matrix.CreateScale(0, 0), TransformOperator.Prepend),
                (Matrix.Identity, TransformOperator.Set),
            ],
            new MarkNode());
        using var renderer = CreateRenderer(root);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(default(Rect)));
            Assert.That(rasterization.IsEmpty, Is.True);
            Assert.That(renderer.HitTest(s_mark.Center), Is.False,
                "A collapsed subtree must not answer a hit its content is not committed for.");
        });
    }

    [Test]
    public void AnAmbientCompositionCommitsTheRequestedRegionOfWhereItDraws()
    {
        (Matrix, TransformOperator)[] pushes =
        [
            (s_scale, TransformOperator.Prepend),
            (s_shift, TransformOperator.Append),
        ];
        Rect drawn = s_mark.TransformToAABB(Compose(pushes));
        var requested = new Rect(drawn.X, drawn.Y, drawn.Width / 2, drawn.Height);

        using RenderNode root = BuildChain(pushes, new MarkNode());
        using var renderer = CreateRenderer(root);

        using RenderNodeRasterization rasterization = renderer.Rasterize(
            Request(s_domain) with { RequestedRegion = requested });

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(requested));
            Assert.That(PaintedBounds(rasterization), Is.EqualTo(requested),
                "the selected half must be the half the composition draws there");
        });
    }

    [Test]
    public void AnAmbientCompositionIsMeasuredTheSameUnderANonOriginTargetDomain()
    {
        (Matrix, TransformOperator)[] pushes =
        [
            (s_translate, TransformOperator.Prepend),
            (Matrix.Identity, TransformOperator.Set),
        ];

        using RenderNode root = BuildChain(pushes, new MarkNode());
        using var renderer = CreateRenderer(root);

        RenderNodeMeasurement measurement = renderer.Measure(Request(new Rect(-16, -16, 96, 96)));
        using RenderNodeRasterization rasterization = renderer.Rasterize(Request(new Rect(-16, -16, 96, 96)));

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_mark));
            Assert.That(PaintedBounds(rasterization), Is.EqualTo(s_mark));
        });
    }

    /// <remarks>
    /// One fragment holds one description, so a scope shared by consumers that contribute the same ambient
    /// resolves once and answers both - which is the ordinary case, and stays recordable. The scope here is
    /// shared through two opacity fragments, so nothing between it and its consumers changes the ambient.
    /// </remarks>
    [Test]
    public void AnAmbientCompositionIsSharedByConsumersThatContributeOneAmbient()
    {
        using RenderNode subtree = BuildChain([(s_shift, TransformOperator.Set)], new MarkNode());
        using var root = new SharedSubtreeNode(subtree, null, null);
        using var renderer = CreateRenderer(root);

        RenderNodeMeasurement measurement = default;
        Assert.Multiple(() =>
        {
            Assert.That(() => measurement = renderer.Measure(), Throws.Nothing);
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_mark.TransformToAABB(s_shift)));
        });
    }

    /// <remarks>
    /// Two ambients have no single answer, and overwriting the first resolution would hand one consumer the
    /// other's matrix without a word - measured, hit tested and drawn through a transform belonging to the
    /// other branch. Resolution says so instead.
    /// </remarks>
    [Test]
    public void AnAmbientCompositionCannotBeSharedByConsumersThatContributeTwoAmbients()
    {
        using RenderNode subtree = BuildChain([(s_shift, TransformOperator.Set)], new MarkNode());
        using var root = new SharedSubtreeNode(subtree, s_scale, s_translate);
        using var renderer = CreateRenderer(root);

        Assert.That(
            () => renderer.Measure(),
            Throws.InvalidOperationException.With.Message.Contains(
                "was reached under two different ambient transforms"));
    }

    /// <remarks>The control: a Prepend is stated in its input's own space, so two ambients ask it nothing.</remarks>
    [Test]
    public void APrependCompositionIsSharedByConsumersThatContributeTwoAmbients()
    {
        using RenderNode subtree = BuildChain([(s_shift, TransformOperator.Prepend)], new MarkNode());
        using var root = new SharedSubtreeNode(subtree, s_scale, s_translate);
        using var renderer = CreateRenderer(root);

        Assert.That(() => renderer.Measure(), Throws.Nothing);
    }

    /// <summary>The composition an <see cref="ImmediateCanvas"/> builds for the same pushes at density 1.</summary>
    private static Matrix Compose((Matrix Matrix, TransformOperator Operator)[] pushes)
    {
        Matrix transform = Matrix.Identity;
        foreach ((Matrix matrix, TransformOperator op) in pushes)
        {
            transform = op switch
            {
                TransformOperator.Prepend => transform.Prepend(matrix),
                TransformOperator.Append => transform.Append(matrix),
                _ => matrix,
            };
        }

        return transform;
    }

    private static RenderNode BuildChain(
        (Matrix Matrix, TransformOperator Operator)[] pushes,
        RenderNode leaf)
    {
        RenderNode current = leaf;
        for (int index = pushes.Length - 1; index >= 0; index--)
        {
            var scope = new TransformRenderNode(pushes[index].Matrix, pushes[index].Operator);
            scope.AddChild(current);
            current = scope;
        }

        return current;
    }

    private static Point Outside(Rect bounds) => new(bounds.Right + 2, bounds.Bottom + 2);

    /// <summary>The bounds of the pixels a rasterization actually painted, in the request's own space.</summary>
    private static Rect PaintedBounds(RenderNodeRasterization rasterization)
    {
        if (rasterization.Bitmap is not { } bitmap)
            return default;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return minX > maxX
            ? default
            : new Rect(
                minX + rasterization.Bounds.X,
                minY + rasterization.Bounds.Y,
                maxX - minX + 1,
                maxY - minY + 1);
    }

    private static RenderNodeRenderRequest Request(Rect domain)
        => new()
        {
            Intent = RenderIntent.Preview,
            TargetDomain = domain,
            OutputScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
        };

    private static RenderNodeRenderer CreateRenderer(RenderNode node) => new(node, Request(s_domain));

    /// <summary>
    /// Publishes one recorded subtree through two consumers, each optionally under a transform of its own so
    /// the two contribute different ambients.
    /// </summary>
    private sealed class SharedSubtreeNode(RenderNode subtree, Matrix? first, Matrix? second) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle inner = context.RecordSubtree(subtree)[0];
            context.Publish(Consume(context, inner, first, 0.5f));
            context.Publish(Consume(context, inner, second, 0.25f));
        }

        private static RenderFragmentHandle Consume(
            RenderNodeContext context,
            RenderFragmentHandle input,
            Matrix? transform,
            float opacity)
        {
            RenderFragmentHandle consumed = context.Opacity(input, opacity);
            return transform is not { } matrix
                ? consumed
                : context.TargetScope(
                    consumed,
                    RenderScopeAmbientTransform.CreateScope(
                        new RenderScopeAmbientTransform(matrix, TransformOperator.Prepend, context.TargetDomain),
                        matrix,
                        capturesBackingTarget: false));
        }
    }

    private sealed class MarkNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                "ambient-transform-mark",
                static (session, _) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_mark);
                    output.Canvas.Use(static canvas => canvas.Clear(s_markColor));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_mark),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
    }
}

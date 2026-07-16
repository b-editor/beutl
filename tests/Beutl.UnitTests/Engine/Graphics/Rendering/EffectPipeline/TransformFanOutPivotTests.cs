using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the per-operation pivot after a fan-out (feature 004): a <see cref="SplitEffect"/> fans the op set
/// into per-tile branches, and a following <see cref="TransformEffect"/> (ApplyToTarget) or
/// <see cref="PathFollowEffect"/> runs its geometry redraw once PER branch. The executor sizes each branch's output
/// buffer by forward-mapping the branch's OWN rect, so the render callback must pivot on that same rect — a callback
/// pivoting on the describe-time union bounds rotates every branch around the union's pivot, out of its allocated
/// buffer, and the composed frame goes black.
/// </summary>
[NonParallelizable]
[TestFixture]
public class TransformFanOutPivotTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void SplitFanOut_ThenTransformRotate180_KeepsEveryBranchInItsBuffer()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap splitOnly = RenderGroup(MakeSplitGroup());
            using Bitmap transformed = RenderGroup(WithTransform(MakeSplitGroup(), rotation: 180f));

            long baseline = CountNonBlack(splitOnly);
            long survived = CountNonBlack(transformed);
            TestContext.WriteLine($"non-black pixels: split-only={baseline}, split+rotate180={survived}");
            Assert.That(baseline, Is.GreaterThan(0), "sanity: the split-only render must produce content");
            // A 180-degree rotation around each tile's own centre keeps every tile inside its buffer, preserving
            // the covered area; the union-pivot regression rotated all four branches out of frame (zero pixels).
            Assert.That(survived, Is.GreaterThanOrEqualTo((long)(baseline * 0.9)),
                "every fan-out branch must pivot on its own rect and stay inside its allocated buffer");
        });
    }

    [Test]
    public void SplitFanOut_ThenPathFollowRotation_KeepsBranchArea()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap splitOnly = RenderGroup(MakeSplitGroup());
            using Bitmap followed = RenderGroup(WithPathFollow(MakeSplitGroup()));

            long baseline = CountNonBlack(splitOnly);
            long survived = CountNonBlack(followed);
            TestContext.WriteLine($"non-black pixels: split-only={baseline}, split+follow={survived}");
            Assert.That(baseline, Is.GreaterThan(0), "sanity: the split-only render must produce content");
            // The follow translation and per-tile rotation clip some content at the frame edge, but the
            // union-pivot regression retained under 10% of the area — half the baseline is a robust floor.
            Assert.That(survived, Is.GreaterThanOrEqualTo((long)(baseline * 0.5)),
                "every fan-out branch must rotate around its own centre and stay substantially in frame");
        });
    }

    private static FilterEffectGroup MakeSplitGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
        });
        return group;
    }

    private static FilterEffectGroup WithTransform(FilterEffectGroup group, float rotation)
    {
        var rotationTransform = new RotationTransform();
        rotationTransform.Rotation.CurrentValue = rotation;
        var effect = new TransformEffect();
        effect.Transform.CurrentValue = rotationTransform;
        group.Children.Add(effect);
        return group;
    }

    private static FilterEffectGroup WithPathFollow(FilterEffectGroup group)
    {
        // A near-vertical path: FollowRotation then rotates each branch ~82deg, so a wrong (union) pivot swings the
        // content far outside the per-branch buffer instead of mostly overlapping it.
        var figure = new PathFigure { StartPoint = { CurrentValue = new Point(0, 0) } };
        figure.Segments.Add(new LineSegment(new Point(8, 60)));
        var geometry = new PathGeometry { Figures = { figure } };
        var effect = new PathFollowEffect
        {
            Geometry = { CurrentValue = geometry },
            Progress = { CurrentValue = 50f },
            FollowRotation = { CurrentValue = true },
        };
        group.Children.Add(effect);
        return group;
    }

    private static Bitmap RenderGroup(FilterEffectGroup group)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        group.Describe(builder, resource);

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [ShapeInput()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    // The same sharp feature at the same within-tile position in every quadrant so all four 2x2 tiles carry content.
    private static RenderNodeOperation ShapeInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(15, 10, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(95, 10, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(15, 70, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(95, 70, 45, 35), Brushes.Resource.Red, null);
            },
            hitTest: s_bounds.Contains);
    }

    private static long CountNonBlack(Bitmap bitmap)
    {
        long count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SkiaSharp.SKColor c = bitmap.SKBitmap.GetPixel(x, y);
                if (c.Red > 8 || c.Green > 8 || c.Blue > 8)
                    count++;
            }
        }

        return count;
    }
}

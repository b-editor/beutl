using System.Text.Json;

using Beutl.Animation;
using Beutl.Animation.Animators;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class MeshWarpTests
{
    [Test]
    public void IdentityDoesNotRecordAnEffect()
    {
        using var context = Record(new MeshWarp(), new Rect(20.5f, 30.25f, 80, 60));
        Assert.That(context.CountItems(), Is.Zero);
    }

    [Test]
    public void NullMeshDoesNotRecordAnEffect()
    {
        var effect = new MeshWarp { Mesh = { Value = null! } };
        using var context = Record(effect, new Rect(0, 0, 80, 60));
        Assert.That(context.CountItems(), Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void ZeroOrNonFiniteStrengthDoesNotRecordAnEffect(float strength)
    {
        var effect = Create(1, 1);
        effect.Strength.Value = strength;
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(0, new Point(-0.5f, 0));
        using var context = Record(effect, new Rect(0, 0, 80, 60));
        Assert.That(context.CountItems(), Is.Zero);
    }

    [Test]
    public void GridHasStructuralEqualityAndRepairsPointCount()
    {
        var moved = new Point(-0.2f, 0.4f);
        var padded = new MeshWarpGrid(1, 1, [moved]);
        var trimmed = new MeshWarpGrid(1, 1, [moved, new(1, 0), new(0, 1), new(1, 1), new(4, 4)]);
        var identity = MeshWarpGrid.CreateIdentity(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(padded, Is.EqualTo(trimmed));
            Assert.That(padded.GetHashCode(), Is.EqualTo(trimmed.GetHashCode()));
            Assert.That(padded.Points.Length, Is.EqualTo(4));
            Assert.That(padded.Points[3], Is.EqualTo(new Point(1, 1)));
            Assert.That(padded, Is.EqualTo(identity.WithPoint(0, 0, moved)));
            Assert.That(padded.IsIdentity(), Is.False);
            Assert.That(identity.IsIdentity(), Is.True);
            Assert.That(MeshWarpGrid.CreateIdentity(-1, 99).Points.Length, Is.EqualTo(2 * 33));
        });
    }

    [Test]
    public void GridAndAnimatedEffectRoundTripThroughJson()
    {
        var effect = Create(2, 3);
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(0, new Point(-0.125f, 0));
        var differentlySizedKey = MeshWarpGrid.CreateIdentity(1, 1).WithPoint(3, new Point(0.8f, 1.2f));
        effect.Mesh.SetKeyFrameAnimation(new KeyFrameAnimation<MeshWarpGrid>
        {
            KeyFrames = { Key(0, effect.Mesh.Value), Key(1, differentlySizedKey) }
        });

        var serialized = CoreSerializer.SerializeToJsonObject(effect);
        var restored = (MeshWarp)CoreSerializer.DeserializeFromJsonObject(serialized, typeof(MeshWarp));
        var keys = restored.Mesh.GetKeyFrameAnimation()!.KeyFrames;
        var padded = JsonSerializer.Deserialize<MeshWarpGrid>("""{"Rows":1,"Columns":1,"Points":["-0.125, 0"]}""");

        Assert.Multiple(() =>
        {
            Assert.That(restored.Rows.Value, Is.EqualTo(2));
            Assert.That(restored.Columns.Value, Is.EqualTo(3));
            Assert.That(restored.Mesh.Value, Is.EqualTo(effect.Mesh.Value));
            Assert.That(keys.Count, Is.EqualTo(2));
            Assert.That(keys[1].Value, Is.EqualTo(differentlySizedKey));
            Assert.That(padded, Is.EqualTo(MeshWarpGrid.CreateIdentity(1, 1).WithPoint(0, new Point(-0.125f, 0))));
            Assert.That(JsonSerializer.Deserialize<MeshWarpGrid>(JsonSerializer.Serialize(effect.Mesh.Value)),
                Is.EqualTo(effect.Mesh.Value));
        });
    }

    [Test]
    public void ResamplingUsesTheSelectedSurfaceAndPreservesRestAndControlPoints()
    {
        var grid = MeshWarpGrid.CreateIdentity(2, 2).WithPoint(1, 1, new Point(0.75f, 0.5f));
        MeshWarpGrid linear = grid.Resample(4, 4);
        MeshWarpGrid smooth = grid.Resample(4, 4, MeshWarpInterpolation.Smooth);
        Assert.Multiple(() =>
        {
            Assert.That(grid.Resample(2, 2), Is.SameAs(grid));
            Assert.That(linear.Points[6], Is.EqualTo(new Point(0.3125f, 0.25f)));
            Assert.That(smooth.Points[6].X, Is.EqualTo(0.25f + 0.25f * 0.5625f * 0.5625f).Within(1e-6));
            Assert.That(smooth.Points[12], Is.EqualTo(grid.Points[4]));
            Assert.That(MeshWarpGrid.Default.Resample(3, 7, MeshWarpInterpolation.Smooth).IsIdentity(0), Is.True);
        });
    }

    [Test]
    public void AnimatorInterpolatesEachPointAndResamplesOntoTheDestinationGrid()
    {
        var animator = AnimatorRegistry.CreateAnimator<MeshWarpGrid>();
        var start = MeshWarpGrid.CreateIdentity(1, 1).WithPoint(1, new Point(1.5f, -0.25f));
        var end = MeshWarpGrid.CreateIdentity(1, 1).WithPoint(2, new Point(-0.25f, 1.5f));
        var midpoint = new MeshWarpGrid(1, 1, [new(0, 0), new(1.25f, -0.125f), new(-0.125f, 1.25f), new(1, 1)]);
        var largerEnd = end.Resample(2, 3);
        Assert.Multiple(() =>
        {
            Assert.That(animator, Is.TypeOf<MeshWarpGridAnimator>());
            Assert.That(animator.Interpolate(0.5f, start, end), Is.EqualTo(midpoint));
            MeshWarpGrid resized = animator.Interpolate(0.5f, start, largerEnd);
            Assert.That(resized.Rows, Is.EqualTo(2));
            Assert.That(resized.Columns, Is.EqualTo(3));
            Assert.That(resized.Points[3], Is.EqualTo(midpoint.Points[1]));
            Assert.That(resized.Points[8].X, Is.EqualTo(midpoint.Resample(2, 3).Points[8].X).Within(1e-6));
            Assert.That(animator.Interpolate(0.5f, null!, null!), Is.EqualTo(MeshWarpGrid.Default));
            Assert.That(animator.Interpolate(0.5f, start, null!), Is.SameAs(start));
        });
    }

    [Test]
    public void AnimatedMeshChangesTheResourceVersionAndWarpBounds()
    {
        var effect = Create(1, 1);
        MeshWarpGrid identity = effect.Mesh.Value;
        MeshWarpGrid translated = MapGrid(identity, p => p + new Point(0.5f, 0));
        effect.Mesh.SetKeyFrameAnimation(new KeyFrameAnimation<MeshWarpGrid>
        {
            KeyFrames = { Key(0, identity), Key(2, translated) }
        });

        using var resource = effect.ToResource(CompositionContext.Default);
        int startVersion = resource.Version;
        var time = new CompositionContext(MediaTime.FromSeconds(1));
        bool bumped = false;
        resource.Reconcile(effect, time, ref bumped);
        using var context = new FilterEffectContext(new Rect(0, 0, 40, 32));
        context.ApplyTransactional(resource);
        Assert.Multiple(() =>
        {
            Assert.That(bumped, Is.True);
            Assert.That(resource.Version, Is.EqualTo(startVersion + 1));
            Assert.That(resource.Mesh.Points[3], Is.EqualTo(new Point(1.25f, 1)));
            Assert.That(context.Bounds, Is.EqualTo(new Rect(10, 0, 40, 32)));
        });

        bumped = false;
        resource.Reconcile(effect, time, ref bumped);
        Assert.That(bumped, Is.False, "a newly interpolated grid with equal points keeps the same version");
    }

    [TestCase(2f, 50f, -0.25f, 1.5f)]
    [TestCase(0.5f, 100f, 0.25f, 0.5f)]
    [TestCase(2f, 150f, -0.75f, 2.5f)]
    [TestCase(2f, -50f, 0.25f, 0.5f)]
    public void BoundsFollowWarpStrength(float scale, float strength, float offset, float size)
    {
        Rect input = new(10.25f, -6.75f, 64, 48);
        var effect = Create(1, 1);
        effect.Mesh.Value = MapGrid(effect.Mesh.Value, p => (p - new Point(0.5f, 0.5f)) * scale + new Point(0.5f, 0.5f));
        effect.Strength.Value = strength;
        using var context = Record(effect, input);
        Assert.That(context.Bounds, Is.EqualTo(new Rect(input.X + input.Width * offset, input.Y + input.Height * offset,
            input.Width * size, input.Height * size)));
    }

    [Test]
    public void SmoothBoundsIncludeOvershootBetweenControlPoints()
    {
        var effect = Create(1, 3, MeshWarpInterpolation.Smooth);
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(0, 1, new Point(1f / 3, -0.25f))
            .WithPoint(0, 2, new Point(2f / 3, -0.25f));
        using var context = Record(effect, new Rect(0, 0, 96, 64));
        Assert.That(context.Bounds, Is.EqualTo(new Rect(0, -18, 96, 82)));
    }

    [TestCase(MeshWarpInterpolation.Linear)]
    [TestCase(MeshWarpInterpolation.Smooth)]
    public void WarpedVerticesSampleTheCorrectSourceQuadrants(MeshWarpInterpolation interpolation)
    {
        var bounds = new Rect(0, 0, 160, 128);
        var effect = Create(2, 2, interpolation);
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(1, 1, new Point(0.8f, 0.4f));

        using EffectTargets targets = ApplyToQuadrants(effect, bounds);
        EffectTarget target = targets.Single();
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            // The moved middle point puts some red pixels in what was the green quadrant.
            AssertColor(bitmap, target, new Point(90, 39), new Rgba(1, 0, 0, 1));
            AssertColor(bitmap, target, new Point(144, 26), new Rgba(0, 1, 0, 1));
            AssertColor(bitmap, target, new Point(30, 103), new Rgba(0, 0, 1, 1));
            AssertColor(bitmap, target, new Point(144, 103), new Rgba(1, 1, 0, 1));
        });
    }

    [TestCase(MeshWarpInterpolation.Linear)]
    [TestCase(MeshWarpInterpolation.Smooth)]
    public void FoldedMeshRendersWithoutNonFinitePixels(MeshWarpInterpolation interpolation)
    {
        var effect = Create(1, 1, interpolation);
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(0, new Point(1.25f, 0.50f))
            .WithPoint(1, new Point(-0.25f, 0.25f));

        using EffectTargets targets = ApplyToQuadrants(effect, new Rect(0, 0, 64, 64));
        using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
        ushort[] bits = bitmap.GetPixelSpan<ushort>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bits.All(value => Half.IsFinite(BitConverter.UInt16BitsToHalf(value))), Is.True);
            Assert.That(bits.Where((_, i) => i % 4 == 3).Count(value => BitConverter.UInt16BitsToHalf(value) > (Half)0.9f),
                Is.GreaterThan(200));
        });
    }

    [Test]
    public void ACollapsedWarpDropsItsTarget()
    {
        var effect = Create(1, 1);
        effect.Mesh.Value = MapGrid(effect.Mesh.Value, p => new Point(p.X, 0.5f));
        using EffectTargets targets = ApplyToQuadrants(effect, new Rect(0, 0, 40, 32));
        Assert.That(targets, Is.Empty);
    }

    [Test]
    public void TextureCoordinatesRespectTheFractionalOriginAndSourceApronAtDifferentScales()
    {
        var input = new Rect(10.25f, 20.25f, 40, 32);
        var effect = Create(1, 1);
        effect.Mesh.Value = MapGrid(effect.Mesh.Value, p => p + new Point(0.125f, -0.125f));

        using EffectTargets targets = ApplyToQuadrants(effect, input, sourceScale: 2, workingScale: 1.5f, apron: 6);
        EffectTarget target = targets.Single();
        var expected = new Rect(15.25f, 16.25f, 40, 32);
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(target.Bounds, Is.EqualTo(expected));
            Assert.That(target.Placement.Scale.Value, Is.EqualTo(1.5f));
            AssertColor(bitmap, target, expected.Position + new Point(1.5f, 1.5f), new Rgba(1, 0, 0, 1));
            AssertColor(bitmap, target, expected.Position + new Point(38.5f, 1.5f), new Rgba(0, 1, 0, 1));
            AssertColor(bitmap, target, expected.Position + new Point(1.5f, 30.5f), new Rgba(0, 0, 1, 1));
            AssertColor(bitmap, target, expected.Position + new Point(38.5f, 30.5f), new Rgba(1, 1, 0, 1));
        });
    }

    [Test]
    public void MaximumResolutionStillRendersTheLastIndexedRows()
    {
        var effect = Create(32, 32, MeshWarpInterpolation.Smooth);
        effect.Mesh.Value = MapGrid(effect.Mesh.Value, p => p + new Point(0.125f, 0.125f));
        using EffectTargets targets = ApplyToQuadrants(effect, new Rect(0, 0, 128, 128));
        EffectTarget target = targets.Single();
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        Assert.That(target.Bounds, Is.EqualTo(new Rect(16, 16, 128, 128)));
        AssertColor(bitmap, target, new Point(142.5f, 143.5f), new Rgba(1, 1, 0, 1));
    }

    [Test]
    public void ResizeResamplesBaseAndKeyframesAndLeavesHistoryReplayToRestoreThem()
    {
        var effect = Create(1, 1);
        MeshWarpGrid baseMesh = effect.Mesh.Value.WithPoint(0, new Point(-0.25f, 0.125f));
        MeshWarpGrid animatedMesh = effect.Mesh.Value.WithPoint(3, new Point(0.75f, 1.25f));
        var key = Key(0, animatedMesh);
        effect.Mesh.Value = baseMesh;
        effect.Mesh.SetKeyFrameAnimation(new KeyFrameAnimation<MeshWarpGrid> { KeyFrames = { key } });

        effect.Rows.Value = 2;
        effect.Columns.Value = 4;
        Assert.Multiple(() =>
        {
            Assert.That(effect.Mesh.Value, Is.EqualTo(baseMesh.Resample(2, 4)));
            Assert.That(key.Value.Value, Is.EqualTo(animatedMesh.Resample(2, 4)));
        });

        using (HistoryReplay.Enter())
        {
            effect.Rows.Value = 1;
            effect.Columns.Value = 1;
        }
        Assert.Multiple(() =>
        {
            Assert.That(effect.Mesh.Value.Rows, Is.EqualTo(2));
            Assert.That(key.Value.Value!.Columns, Is.EqualTo(4));
        });
    }

    [Test]
    public void SourceApronDoesNotBleedIntoEdgePixelsWhenStretching()
    {
        var input = new Rect(10, 20, 40, 32);
        var effect = Create(1, 1);
        // Stretch the mesh 2x so the outer destination pixels sample within 0.25 texels of the semantic boundary.
        // Without subsetting the source shader to the semantic bounds, Clamp would sample into the magenta apron.
        effect.Mesh.Value = MapGrid(effect.Mesh.Value, p => p * 2f);

        using EffectTargets targets = ApplyToQuadrants(effect, input, sourceScale: 1, workingScale: 1, apron: 6);
        EffectTarget target = targets.Single();
        var expected = new Rect(10, 20, 80, 64);
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            AssertColor(bitmap, target, expected.Position + new Point(0.5f, 0.5f), new Rgba(1, 0, 0, 1));
            AssertColor(bitmap, target, expected.Position + new Point(79.5f, 0.5f), new Rgba(0, 1, 0, 1));
            AssertColor(bitmap, target, expected.Position + new Point(0.5f, 63.5f), new Rgba(0, 0, 1, 1));
            AssertColor(bitmap, target, expected.Position + new Point(79.5f, 63.5f), new Rgba(1, 1, 0, 1));
        });
    }

    [Test]
    public void ExpressionDrivenMeshIgnoresHandleEditsAndInsertedKeyFramesUseSplineEasing()
    {
        var animated = Create(1, 1);
        animated.Mesh.SetKeyFrameAnimation(new KeyFrameAnimation<MeshWarpGrid>
        {
            KeyFrames = { Key(0, animated.Mesh.Value) }
        });
        var shape = new RectShape { Width = { Value = 100 }, Height = { Value = 100 }, FilterEffect = { Value = animated } };
        animated.MoveEditingHandle("0", new Point(-10, 0), MediaTime.FromSeconds(1));
        var inserted = (KeyFrame<MeshWarpGrid>)animated.Mesh.GetKeyFrameAnimation()!.KeyFrames[1];
        Assert.That(inserted.Easing.Value, Is.TypeOf<Beutl.Animation.Easings.SplineEasing>());
        GC.KeepAlive(shape);

        var exprEffect = Create(1, 1);
        MeshWarpGrid before = exprEffect.Mesh.Value;
        exprEffect.Mesh.Expression = new ConstantMeshExpression(before);
        var exprShape = new RectShape { Width = { Value = 100 }, Height = { Value = 100 }, FilterEffect = { Value = exprEffect } };
        exprEffect.MoveEditingHandle("0", new Point(-20, 0), MediaTime.Zero);
        Assert.That(exprEffect.Mesh.Value, Is.EqualTo(before));
        GC.KeepAlive(exprShape);
    }

    private sealed class ConstantMeshExpression(MeshWarpGrid value) : IExpression<MeshWarpGrid>
    {
        public string ExpressionString => string.Empty;
        public MeshWarpGrid Evaluate(ExpressionContext context) => value;
        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    [Test]
    public void UnrenderedHandlesUseTheOwningDrawablesMeasuredSizeAndIgnoreStrength()
    {
        var effect = Create(1, 1);
        effect.Strength.Value = 0;
        effect.Mesh.Value = effect.Mesh.Value.WithPoint(0, new Point(-0.25f, 0));
        var shape = new RectShape { Width = { Value = 100 }, Height = { Value = 64 }, FilterEffect = { Value = effect } };

        Assert.That(effect.GetEditingHandles(MediaTime.Zero).Select(handle => handle.Position),
            Is.EqualTo(new Point[] { new(-25, 0), new(100, 0), new(0, 64), new(100, 64) }));
        GC.KeepAlive(shape);
    }

    private static MeshWarp Create(int rows, int columns, MeshWarpInterpolation interpolation = MeshWarpInterpolation.Linear)
        => new()
        {
            Rows = { Value = rows },
            Columns = { Value = columns },
            Interpolation = { Value = interpolation },
        };

    private static MeshWarpGrid MapGrid(MeshWarpGrid grid, Func<Point, Point> map)
        => new(grid.Rows, grid.Columns, grid.Points.Select(map));

    private static KeyFrame<MeshWarpGrid> Key(double seconds, MeshWarpGrid grid)
        => new() { KeyTime = { Value = MediaTime.FromSeconds(seconds) }, Value = { Value = grid } };

    private static FilterEffectContext Record(MeshWarp effect, Rect bounds)
    {
        using var resource = effect.ToResource(CompositionContext.Default);
        var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        return context;
    }

    private static EffectTargets ApplyToQuadrants(MeshWarp effect, Rect bounds, float sourceScale = 1, float workingScale = 1, int apron = 0)
    {
        PixelRect footprint = PixelRect.FromRect(bounds, sourceScale);
        footprint = new PixelRect(footprint.X - apron, footprint.Y - apron, footprint.Width + 2 * apron, footprint.Height + 2 * apron);
        using RenderTarget backing = CpuRenderTargets.Create(footprint.Width, footprint.Height);
        SKCanvas canvas = backing.Value.Canvas;
        canvas.Clear(apron > 0 ? SKColors.Magenta : SKColors.Transparent);

        float x = bounds.X * sourceScale - footprint.X;
        float y = bounds.Y * sourceScale - footprint.Y;
        float w = bounds.Width * sourceScale / 2;
        float h = bounds.Height * sourceScale / 2;
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(x, y, w, h, paint);
        paint.Color = SKColors.Lime;
        canvas.DrawRect(x + w, y, w, h, paint);
        paint.Color = SKColors.Blue;
        canvas.DrawRect(x, y + h, w, h, paint);
        paint.Color = SKColors.Yellow;
        canvas.DrawRect(x + w, y + h, w, h, paint);
        canvas.Flush();

        var targets = new EffectTargets { new EffectTarget(backing, bounds, EffectiveScale.At(sourceScale), footprint) };
        try
        {
            using var context = Record(effect, bounds);
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview, RenderRequestPurpose.Auxiliary,
                drawableBrushMaterializer: null, workingScale: workingScale, maxWorkingScale: workingScale);
            executor.Apply(context);
            executor.Flush(false);
            return targets;
        }
        catch
        {
            targets.Dispose();
            throw;
        }
    }

    private static void AssertColor(Bitmap bitmap, EffectTarget target, Point position, Rgba expected)
    {
        var placement = target.Placement;
        Point offset = (position - placement.RasterBounds.Position) * placement.Scale.Value;
        Rgba actual = ImageMetrics.ReadPixel(bitmap, (int)offset.X, (int)offset.Y);
        Assert.That(actual, Is.EqualTo(expected), $"at {position}");
    }
}

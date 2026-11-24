using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

// Mesh Warp and Puppet draw triangles, which have no coverage anti-aliasing. These check that an opaque picture's
// outline still comes out anti-aliased where the warp slants it.
[TestFixture]
public sealed class WarpEdgeAntialiasingTests
{
    private const int Surface = 200;
    // A pixel is partly covered strictly between these alphas.
    private const float PartlyCovered = 0.02f;

    [Test]
    public void MeshWarpSlantedEdgeMatchesAnAntialiasedPath()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap warped = FilteredShapeHarness.Render(
                new Rect(0, 0, Surface, Surface), CreateSlantingMeshWarp(), fullBleed: true, surface: Surface,
                targetDomain: new Rect(0, 0, Surface, Surface));
            // The top-right corner moves to x = 140, so the right edge runs from (140, 0) to (200, 200).
            using Bitmap path = RenderAntialiasedPath([new(0, 0), new(140, 0), new(200, 200), new(0, 200)]);

            var problems = new List<string>();
            for (int y = 10; y < 190; y++)
            {
                float edge = 140 + 0.3f * (y + 0.5f);
                float[] alphas = ReadAlphas(warped, y, 100, Surface);
                CheckRamp(alphas, 100, y, problems);
                for (int x = 100; x < Surface; x++)
                {
                    float alpha = alphas[x - 100];
                    if (x + 0.5f <= edge - 1.5f && alpha != 1)
                        problems.Add($"({x}, {y}) inside the edge has alpha {alpha}");
                    if (x + 0.5f >= edge + 1.5f && alpha != 0)
                        problems.Add($"({x}, {y}) outside the edge has alpha {alpha}");

                    float expected = ImageMetrics.AlphaAt(path, x, y);
                    if (MathF.Abs(alpha - expected) > 0.06f)
                        problems.Add($"({x}, {y}) has alpha {alpha}, the path {expected}");
                }
            }

            Assert.That(problems, Is.Empty, Summarize(problems));
        });
    }

    [Test]
    public void PuppetSlantedEdgeFallsOffOverAboutOnePixel()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Lifting the right pin while the left one holds raises the right of the picture, so its top edge leans.
            var held = new Point(0.25f, 0.5f);
            var effect = new Puppet { Pins = { Pin(held, held), Pin(new Point(0.75f, 0.5f), new Point(0.75f, 0.3f)) } };
            using Bitmap bitmap = FilteredShapeHarness.Render(
                new Rect(40, 40, 120, 120), effect, fullBleed: true, surface: Surface,
                targetDomain: new Rect(0, 0, Surface, Surface));

            // Columns 40 to 125 cross only the leaning top edge between y = 70 and the top of the surface.
            var problems = new List<string>();
            int columns = 0;
            int partlyCoveredColumns = 0;
            for (int x = 40; x <= 125; x++)
            {
                float[] alphas = ReadColumnUpward(bitmap, x, 70);
                columns++;
                if (alphas.Any(alpha => alpha is > PartlyCovered and < 1 - PartlyCovered))
                    partlyCoveredColumns++;

                CheckMonotonic(alphas, 70, x, problems);
                int inside = Array.FindIndex(alphas, alpha => alpha < 1);
                int outside = Array.FindIndex(alphas, alpha => alpha == 0);
                if (inside < 5 || outside < 0)
                    problems.Add($"column {x} does not cross the edge in the scanned range");
                else if (outside - inside > 2)
                    problems.Add($"column {x} falls off over {outside - inside} pixels from y = {70 - inside}");
            }

            // A crossing right on a pixel boundary leaves its column without a partly covered pixel, as a path's
            // does, and the edge curves, so most columns are counted rather than all.
            Assert.That(problems, Is.Empty, Summarize(problems));
            Assert.That(partlyCoveredColumns, Is.GreaterThanOrEqualTo(columns * 3 / 4),
                $"{partlyCoveredColumns} of {columns} columns have a partly covered pixel");
        });
    }

    [Test]
    public void SplitTargetsFadeOnlyTheirOuterEdges()
    {
        // Two opaque halves meet at x = 100 and warp in one shared space.
        using RenderTarget leftBacking = CreateOpaqueBacking(100, Surface);
        using RenderTarget rightBacking = CreateOpaqueBacking(100, Surface);
        using var targets = new EffectTargets
        {
            new EffectTarget(leftBacking, new Rect(0, 0, 100, Surface), EffectiveScale.At(1), new PixelRect(0, 0, 100, Surface)),
            new EffectTarget(rightBacking, new Rect(100, 0, 100, Surface), EffectiveScale.At(1), new PixelRect(100, 0, 100, Surface)),
        };
        using (var resource = CreateSlantingMeshWarp().ToResource(CompositionContext.Default))
        using (var context = new FilterEffectContext(new Rect(0, 0, Surface, Surface)))
        using (var builder = new SKImageFilterBuilder())
        {
            context.ApplyTransactional(resource);
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null, workingScale: 1, maxWorkingScale: 1);
            executor.Apply(context);
            executor.Flush(false);
        }

        using RenderTarget composite = CpuRenderTargets.Create(Surface, Surface);
        SKCanvas canvas = composite.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        foreach (EffectTarget tile in targets)
        {
            using SKImage image = tile.RenderTarget!.Value.Snapshot();
            canvas.DrawImage(image, tile.Placement.RasterBounds.X, tile.Placement.RasterBounds.Y, SKSamplingOptions.Default);
        }
        canvas.Flush();
        using Bitmap bitmap = composite.Snapshot();

        var problems = new List<string>();
        for (int y = 10; y < 190; y++)
        {
            float edge = 140 + 0.3f * (y + 0.5f);
            float[] alphas = ReadAlphas(bitmap, y, 0, Surface);
            // The seam between the halves stays as opaque as the rest of the inside, from a pixel in from the left side.
            for (int x = 1; x + 0.5f <= edge - 1.5f; x++)
            {
                if (alphas[x] != 1)
                    problems.Add($"({x}, {y}) inside has alpha {alphas[x]}");
            }

            // Only near the slanted edge, so a partly covered pixel at the left side cannot stand in for it.
            int near = Math.Max(0, (int)MathF.Floor(edge - 1.5f));
            int far = Math.Min(Surface - 1, (int)MathF.Ceiling(edge + 1.5f));
            if (!alphas[near..(far + 1)].Any(alpha => alpha is > PartlyCovered and < 1 - PartlyCovered))
                problems.Add($"row {y} has no partly covered pixel at its slanted outer edge");
        }

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(problems, Is.Empty, Summarize(problems));
        });
    }

    [Test]
    public void FoldedMeshKeepsThePictureUnderItsFadingSide()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The first column stretches from x = 0 to 200, and the second folds back over it, so the source's right
            // side fades out at x = 100 on top of the first column, which covers everything around it.
            MeshWarpGrid grid = MeshWarpGrid.CreateIdentity(1, 2)
                .WithPoint(0, 1, new Point(1, 0))
                .WithPoint(1, 1, new Point(1, 1))
                .WithPoint(0, 2, new Point(0.5f, 0))
                .WithPoint(1, 2, new Point(0.5f, 1));
            var effect = new MeshWarp
            {
                Rows = { Value = 1 },
                Columns = { Value = 2 },
                Interpolation = { Value = MeshWarpInterpolation.Linear },
                Mesh = { Value = grid },
            };
            using Bitmap bitmap = FilteredShapeHarness.Render(
                new Rect(0, 0, Surface, Surface), effect, fullBleed: true, surface: Surface,
                targetDomain: new Rect(0, 0, Surface, Surface));

            var problems = new List<string>();
            for (int y = 10; y < 190; y++)
            {
                // From a pixel in from the left side to the fold at the right.
                for (int x = 1; x < Surface; x++)
                {
                    float alpha = ImageMetrics.AlphaAt(bitmap, x, y);
                    if (alpha < 0.998f)
                        problems.Add($"({x}, {y}) has alpha {alpha}");
                }
            }

            Assert.That(problems, Is.Empty, Summarize(problems));
        });
    }

    [Test]
    public void PinsStillAttachOnlyToThePicture()
    {
        // The margin past the outline only draws. A point just outside the picture still misses its mesh.
        var bounds = new Rect(20, 30, 100, 80);
        using RenderTarget input = CreateOpaqueBacking(100, 80);
        using var target = new EffectTarget(input, new Rect(bounds.Size));
        Point[] anchors = [new(0.5f, 0.5f)];
        var (mesh, deformer) = new PuppetMeshCache().GetOrCreate(target, anchors)!.Value;
        var placement = new Puppet.PinPlacement(bounds, mesh, deformer.Deform(bounds.Size, anchors));

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Outline, Is.Not.Empty);
            Assert.That(mesh.Outline.All(edge => OnBounds(mesh.Rest[edge.Start]) && OnBounds(mesh.Rest[edge.End])), Is.True,
                "a full-bleed picture's outline is its bounds");
            Assert.That(placement.CreatePin(new Point(bounds.X - 0.25f, bounds.Y + 40)), Is.Null);
            Assert.That(placement.CreatePin(new Point(bounds.Right + 0.25f, bounds.Y + 40)), Is.Null);
            Assert.That(placement.CreatePin(new Point(bounds.X + 0.25f, bounds.Y + 40)), Is.Not.Null);
        });

        static bool OnBounds(Vector2 point) => point.X is 0 or 100 || point.Y is 0 or 80;
    }

    private static MeshWarp CreateSlantingMeshWarp()
        => new()
        {
            Rows = { Value = 1 },
            Columns = { Value = 1 },
            Interpolation = { Value = MeshWarpInterpolation.Linear },
            Mesh = { Value = MeshWarpGrid.CreateIdentity(1, 1).WithPoint(0, 1, new Point(0.7f, 0)) },
        };

    private static PuppetPin Pin(Point rest, Point position)
        => new() { RestPosition = { Value = rest }, Position = { Value = position } };

    private static RenderTarget CreateOpaqueBacking(int width, int height)
    {
        RenderTarget backing = CpuRenderTargets.Create(width, height);
        backing.Value.Canvas.Clear(SKColors.White);
        backing.Value.Canvas.Flush();
        return backing;
    }

    private static Bitmap RenderAntialiasedPath(SKPoint[] polygon)
    {
        using RenderTarget target = CpuRenderTargets.Create(Surface, Surface);
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var builder = new SKPathBuilder();
        builder.AddPoly(polygon, close: true);
        using SKPath path = builder.Detach();
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawPath(path, paint);
        canvas.Flush();
        return target.Snapshot();
    }

    private static float[] ReadAlphas(Bitmap bitmap, int y, int from, int to)
    {
        var alphas = new float[to - from];
        for (int x = from; x < to; x++)
            alphas[x - from] = ImageMetrics.AlphaAt(bitmap, x, y);
        return alphas;
    }

    // From a row inside the picture up to the top of the surface.
    private static float[] ReadColumnUpward(Bitmap bitmap, int x, int from)
    {
        var alphas = new float[from + 1];
        for (int y = from; y >= 0; y--)
            alphas[from - y] = ImageMetrics.AlphaAt(bitmap, x, y);
        return alphas;
    }

    // Across the edge the alpha only falls, through a partly covered pixel, to zero.
    private static void CheckRamp(float[] alphas, int from, int line, List<string> problems)
    {
        CheckMonotonic(alphas, from, line, problems);
        if (!alphas.Any(alpha => alpha is > PartlyCovered and < 1 - PartlyCovered))
        {
            problems.Add($"line {line} has no partly covered pixel: "
                + string.Join(", ", alphas.Where(alpha => alpha is > 0 and < 1)));
        }
    }

    private static void CheckMonotonic(float[] alphas, int from, int line, List<string> problems)
    {
        for (int i = 1; i < alphas.Length; i++)
        {
            if (alphas[i] > alphas[i - 1] + 1e-3f)
                problems.Add($"line {line} rises to {alphas[i]} after {alphas[i - 1]}, {i} pixels from {from}");
        }

        if (alphas[^1] != 0)
            problems.Add($"line {line} does not reach zero");
    }

    private static string Summarize(List<string> problems)
        => $"{problems.Count} problems: {string.Join("; ", problems.Take(12))}";
}

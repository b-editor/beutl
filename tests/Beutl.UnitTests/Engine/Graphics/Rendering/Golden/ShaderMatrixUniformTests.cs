using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// SkSL reads matrix uniform data column-major. A canonical SKMatrix value that kept Skia's row-major storage
// order would reach the shader transposed, which for an affine matrix silently drops the translation column.
[NonParallelizable]
[TestFixture]
public class ShaderMatrixUniformTests
{
    private const int Width = 200;
    private const int Height = 200;

    // Chosen so the shifted rect stays fully inside the frame and no probe lands on an antialiased edge.
    private const int TranslationX = 60;

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SkMatrixUniform_TranslatesSampledCoordinatesByItsTranslationColumn()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var identity = new SampleOffsetNode(SKMatrix.CreateIdentity());
            using var translated = new SampleOffsetNode(SKMatrix.CreateTranslation(TranslationX, 0));

            using Bitmap unshifted = Render(identity);
            using Bitmap shifted = Render(translated);

            (int dx, int dy, double error) = BestIntegerShift(unshifted, shifted, 96);
            TestContext.WriteLine(
                $"identity coverage={CoveredPixelCount(unshifted)} shifted coverage={CoveredPixelCount(shifted)} "
                + $"best shift=({dx},{dy}) error={error:F6} error@0={ShiftedMeanAbsoluteError(unshifted, shifted, 0, 0):F6}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("unshifted", unshifted), ("shifted", shifted)),
                    Is.Null);

                // Non-vacuity: an empty source would match at every candidate shift.
                Assert.That(
                    CoveredPixelCount(unshifted),
                    Is.GreaterThan(Width * Height / 8),
                    "the identity-matrix render must contain substantial opaque coverage.");

                // A dropped translation column leaves the best match at (0, 0).
                Assert.That(dx, Is.EqualTo(-TranslationX));
                Assert.That(dy, Is.Zero);

                // An integer translation resolves to exact texel fetches; measured 0.000000 on Vulkan.
                Assert.That(error, Is.LessThan(0.005));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SkMatrixUniform_MatchesAnExplicitColumnMajorFloatSequence()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var matrix = SKMatrix.CreateScaleTranslation(0.5f, 2f, TranslationX, 24);
            using var viaMatrix = new SampleOffsetNode(matrix);
            using var viaFloats = new SampleOffsetNode(
            [
                matrix.ScaleX, matrix.SkewY, matrix.Persp0,
                matrix.SkewX, matrix.ScaleY, matrix.Persp1,
                matrix.TransX, matrix.TransY, matrix.Persp2,
            ]);

            using Bitmap fromMatrix = Render(viaMatrix);
            using Bitmap fromFloats = Render(viaFloats);

            GoldenImageHarness.AssertByteIdentical(fromFloats, fromMatrix);
        });
    }

    private static Bitmap Render(RenderNode node)
    {
        using RenderTarget target = RenderTarget.Create(Width, Height)
            ?? throw new InvalidOperationException("Could not allocate the matrix-uniform target.");
        using (var canvas = new ImmediateCanvas(target, 1, 1, new Size(Width, Height)))
        {
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        TargetDomain = new Rect(0, 0, Width, Height),
                        OutputScale = 1,
                        MaxWorkingScale = 1,
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    },
                });
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    /// <summary>
    /// Returns the integer shift within <paramref name="radius"/> that best aligns <paramref name="shifted"/>
    /// onto <paramref name="reference"/>, together with the mean absolute error at that shift.
    /// </summary>
    private static (int Dx, int Dy, double Error) BestIntegerShift(Bitmap reference, Bitmap shifted, int radius)
    {
        int bestDx = 0;
        int bestDy = 0;
        double bestError = double.PositiveInfinity;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                double error = ShiftedMeanAbsoluteError(reference, shifted, dx, dy);
                if (error < bestError)
                {
                    bestError = error;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        return (bestDx, bestDy, bestError);
    }

    // Every output pixel contributes, with out-of-frame reference samples read as transparent. Averaging over
    // the overlap instead would make a large shift with a near-empty overlap the cheapest match.
    private static double ShiftedMeanAbsoluteError(Bitmap reference, Bitmap shifted, int dx, int dy)
    {
        double sum = 0;
        for (int y = 0; y < Height; y++)
        {
            int sourceY = y - dy;
            bool rowInRange = sourceY >= 0 && sourceY < Height;
            ReadOnlySpan<ushort> referenceRow = rowInRange ? reference.GetRow<ushort>(sourceY) : default;
            ReadOnlySpan<ushort> shiftedRow = shifted.GetRow<ushort>(y);
            for (int x = 0; x < Width; x++)
            {
                int sourceX = x - dx;
                bool inRange = rowInRange && sourceX >= 0 && sourceX < Width;
                for (int channel = 0; channel < 4; channel++)
                {
                    float a = inRange
                        ? (float)BitConverter.UInt16BitsToHalf(referenceRow[(sourceX * 4) + channel])
                        : 0f;
                    float b = (float)BitConverter.UInt16BitsToHalf(shiftedRow[(x * 4) + channel]);
                    sum += Math.Abs(a - b);
                }
            }
        }

        return sum / ((double)Width * Height * 4);
    }

    private static long CoveredPixelCount(Bitmap bitmap)
    {
        long count = 0;
        for (int y = 0; y < Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < Width; x++)
            {
                if ((float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]) > 0.5f)
                    count++;
            }
        }

        return count;
    }

    // Samples the upstream source through a float3x3 uniform. The bounds contract keeps the full frame so a
    // translated sample stays inside the stage output instead of being cropped away.
    private sealed class SampleOffsetNode : RenderNode
    {
        private const string Source =
            """
            uniform shader src;
            uniform float3x3 xform;

            half4 main(float2 coord) {
                float3 mapped = xform * float3(coord, 1.0);
                return src.eval(mapped.xy);
            }
            """;

        private static readonly RenderBoundsContract s_bounds = RenderBoundsContract.CreateFullInput(
            static input => input.Inflate(96),
            "matrix-uniform-probe");

        private readonly RectGeometry _geometry;
        private readonly Geometry.Resource _geometryResource;
        private readonly Brush.Resource _fillResource;
        private readonly GeometryRenderNode _source;
        private readonly ShaderDescription _description;

        public SampleOffsetNode(SKMatrix matrix)
            : this(bindings => bindings.Uniform("xform", matrix))
        {
        }

        public SampleOffsetNode(float[] columnMajor)
            : this(bindings => bindings.Uniform("xform", columnMajor))
        {
        }

        private SampleOffsetNode(Action<ShaderBindingBuilder> bindings)
        {
            _geometry = new RectGeometry
            {
                Width = { CurrentValue = 96 },
                Height = { CurrentValue = 96 },
            };
            _geometryResource = _geometry.ToResource(CompositionContext.Default);
            _fillResource = new SolidColorBrush(new Color(255, 220, 120, 40))
                .ToResource(CompositionContext.Default);
            _source = new GeometryRenderNode(_geometryResource, _fillResource, null);
            _description = ShaderDescription.WholeSource(Source, s_bounds, bindings);
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.RecordNode(_source, [])[0];
            context.Publish(context.Shader(source, _description));
        }

        protected override void OnDispose(bool disposing)
        {
            _source.Dispose();
            _fillResource.Dispose();
            _geometryResource.Dispose();
            base.OnDispose(disposing);
        }
    }
}

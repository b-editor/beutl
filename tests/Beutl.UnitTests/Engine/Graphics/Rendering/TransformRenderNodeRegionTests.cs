using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class TransformRenderNodeRegionTests
{
    // Red from x = 0 to 50 and blue to 100, made at a density of 1 whatever is asked of it, as an image is, and only where a
    // request needs it, as a shader graph's image is.
    private static readonly Rect s_picture = new(0, 0, 100, 100);

    // Scaled up 1.5 times, the picture is a value enlarged with filtering. The part's left edge at x = 75 is where red
    // meets blue, so its outermost pixels read the red texels just past what the scale maps them to.
    [Test]
    public void A_value_scaled_up_draws_the_pixels_of_the_whole_up_to_the_edges_of_a_part()
        => AssertPartDrawsTheWhole(
            static () => new TransformRenderNode(Matrix.CreateScale(1.5f, 1.5f), TransformOperator.Prepend), s_scaledPart);

    // The same scale, pushed by a public scope before it replays its input, as an extension may. The engine cannot see
    // the matrix, but the scope declares that it replays the input onto a grid of its own, the default (#4086).
    [Test]
    public void A_value_scaled_up_by_a_public_scope_draws_the_pixels_of_the_whole_up_to_the_edges_of_a_part()
        => AssertPartDrawsTheWhole(static () => new PublicScaleNode(), s_scaledPart);

    // An identity transform declares that it keeps its input's grid, yet drawn 1.5 times as dense, the picture is enlarged
    // with filtering all the same. The part's left edge at x = 50 is where red meets blue.
    [Test]
    public void A_value_drawn_denser_under_an_identity_transform_draws_the_pixels_of_the_whole_up_to_the_edges_of_a_part()
        => AssertPartDrawsTheWhole(
            static () => new TransformRenderNode(Matrix.Identity, TransformOperator.Prepend), new Rect(50, 16, 40, 60), 1.5f);

    // With nothing above it, the picture is drawn into the destination 1.5 times as dense all the same (#4096).
    [Test]
    public void A_value_drawn_denser_with_nothing_above_it_draws_the_pixels_of_the_whole_up_to_the_edges_of_a_part()
        => AssertPartDrawsTheWhole(static () => new ContainerRenderNode(), new Rect(50, 16, 40, 60), 1.5f);

    // A push declares that it keeps its input's grid, and replays the picture into the destination as it is (#4096).
    [Test]
    public void A_value_drawn_denser_under_a_push_draws_the_pixels_of_the_whole_up_to_the_edges_of_a_part()
        => AssertPartDrawsTheWhole(static () => new PushRenderNode(), new Rect(50, 16, 40, 60), 1.5f);

    private static readonly Rect s_scaledPart = new(75, 15, 60, 60);

    private static void AssertPartDrawsTheWhole(Func<ContainerRenderNode> createScale, Rect region, float outputScale = 1)
    {
        (Rect wholeBounds, ushort[] whole, int wholeWidth) = Rasterize(createScale, null, outputScale);

        (Rect partBounds, ushort[] part, int partWidth) = Rasterize(createScale, region, outputScale);

        Assert.That(partBounds, Is.EqualTo(region));
        int offsetX = (int)MathF.Round((partBounds.X - wholeBounds.X) * outputScale);
        int offsetY = (int)MathF.Round((partBounds.Y - wholeBounds.Y) * outputScale);
        (float Difference, int X, int Y) largest = default;
        for (int y = 0; y < part.Length / 4 / partWidth; y++)
        {
            for (int x = 0; x < partWidth; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    float difference = MathF.Abs(
                        (float)BitConverter.UInt16BitsToHalf(part[(((y * partWidth) + x) * 4) + channel])
                        - (float)BitConverter.UInt16BitsToHalf(whole[((((y + offsetY) * wholeWidth) + x + offsetX) * 4) + channel]));
                    if (difference > largest.Difference)
                        largest = (difference, x, y);
                }
            }
        }

        Assert.That(
            largest.Difference,
            Is.LessThanOrEqualTo(0.01f),
            $"at ({partBounds.X + (largest.X / outputScale)}, {partBounds.Y + (largest.Y / outputScale)})");
    }

    private static (Rect Bounds, ushort[] Pixels, int Width) Rasterize(
        Func<ContainerRenderNode> createScale, Rect? requestedRegion, float outputScale)
    {
        using ContainerRenderNode scale = createScale();
        scale.AddChild(new PictureNode());
        using var renderer = new RenderNodeRenderer(scale, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            RequestedRegion = requestedRegion,
            OutputScale = outputScale,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        }, new CpuRenderTargetFactory());

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new InvalidOperationException("The scaled picture produced no bitmap.");
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (rasterization.Bounds, bitmap.GetPixelSpan<ushort>().ToArray(), bitmap.Width);
    }

    // Scales its input 1.5 times by pushing the matrix before it replays the input, through the public description.
    private sealed class PublicScaleNode : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.PublishMappedInputs(
                TargetScopeDescription.Create(
                    1.5f,
                    static (session, scale) => session.Canvas.Use(canvas =>
                    {
                        using (canvas.PushTransform(Matrix.CreateScale(scale, scale)))
                            session.ReplayInput();
                    }),
                    RenderBoundsContract.Create(
                        1.5f,
                        static (scale, bounds) => bounds.TransformToAABB(Matrix.CreateScale(scale, scale)),
                        static (scale, bounds) => bounds.TransformToAABB(Matrix.CreateScale(1 / scale, 1 / scale))),
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply),
                static (context, input, value) => context.TargetScope(input, value));
    }

    private sealed class PictureNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                "red-and-blue",
                static (session, _) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
                    output.Canvas.Use(static canvas =>
                    {
                        using (canvas.PushClip(new Rect(0, 0, 50, 100)))
                            canvas.Clear(Colors.Red);
                        using (canvas.PushClip(new Rect(50, 0, 50, 100)))
                            canvas.Clear(Colors.Blue);
                    });
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_picture),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static _ => 1f))));
    }
}

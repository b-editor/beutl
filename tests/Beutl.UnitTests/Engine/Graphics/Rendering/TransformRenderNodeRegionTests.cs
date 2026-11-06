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
    {
        var region = new Rect(75, 15, 60, 60);
        (Rect wholeBounds, ushort[] whole, int wholeWidth) = Rasterize(null);

        (Rect partBounds, ushort[] part, int partWidth) = Rasterize(region);

        Assert.That(partBounds, Is.EqualTo(region));
        int offsetX = (int)(partBounds.X - wholeBounds.X);
        int offsetY = (int)(partBounds.Y - wholeBounds.Y);
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
                        largest = (difference, x + (int)partBounds.X, y + (int)partBounds.Y);
                }
            }
        }

        Assert.That(largest.Difference, Is.LessThanOrEqualTo(0.01f), $"at ({largest.X}, {largest.Y})");
    }

    private static (Rect Bounds, ushort[] Pixels, int Width) Rasterize(Rect? requestedRegion)
    {
        using var scale = new TransformRenderNode(Matrix.CreateScale(1.5f, 1.5f), TransformOperator.Prepend);
        scale.AddChild(new PictureNode());
        using var renderer = new RenderNodeRenderer(scale, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            RequestedRegion = requestedRegion,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        }, new CpuRenderTargetFactory());

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new InvalidOperationException("The scaled picture produced no bitmap.");
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (rasterization.Bounds, bitmap.GetPixelSpan<ushort>().ToArray(), bitmap.Width);
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

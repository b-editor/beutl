using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

// On the GPU a paint's color reached the target as 8 bits in the target's color space, which in the linear working space
// rounded a dark color to a step of 1/255: sRGB 8, 13 and 17 all drew as 13 (#4288). A solid brush now gives its color
// through a shader.
[TestFixture]
[NonParallelizable]
public sealed class SolidColorPrecisionTests
{
    private const int Patch = 20;
    private static readonly byte[] s_grays = [4, 8, 13, 17, 22, 29, 40, 51, 64, 96, 128, 255];

    [Test]
    public void Dark_solid_fills_keep_their_sRGB_value_on_the_GPU()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene(Patch * s_grays.Length, Patch, "dark-fills");
            for (int i = 0; i < s_grays.Length; i++)
            {
                var rect = new RectShape
                {
                    Width = { Value = Patch },
                    Height = { Value = Patch },
                    Fill = { Value = new SolidColorBrush(Color.FromRgb(s_grays[i], s_grays[i], s_grays[i])) },
                    AlignmentX = { Value = AlignmentX.Left },
                    AlignmentY = { Value = AlignmentY.Top },
                };
                rect.Transform.Value = new TranslateTransform(i * Patch, 0);
                var element = new Element { ZIndex = { Value = i }, Length = { Value = MediaTime.FromSeconds(1) } };
                element.Objects.Add(rect);
                scene.Children.Add(element);
            }

            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery) { CacheOptions = RenderCacheOptions.Disabled };
            renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.Zero));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);

            Assert.Multiple(() =>
            {
                for (int i = 0; i < s_grays.Length; i++)
                {
                    Bgra8888 pixel = srgb.GetRow<Bgra8888>(Patch / 2)[(i * Patch) + (Patch / 2)];
                    Assert.That(new int[] { pixel.R, pixel.G, pixel.B }, Is.All.EqualTo(s_grays[i]).Within(1),
                        $"sRGB {s_grays[i]} drew as {pixel.R}");
                }
            });
        });
    }

    // Skia leaves a paint's shader out when the paint goes around a layer, as with an SVG picture, or when it draws a
    // color image, so a solid fill there still gives only its alpha.
    [Test]
    public void A_solid_fill_around_a_picture_or_an_image_gives_only_its_alpha()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var info = new SKImageInfo(4, 4, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
            using SKSurface surface = SKSurface.Create(context.SkiaContext, false, info);
            using var recorder = new SKPictureRecorder();
            using (var blue = new SKPaint { Color = SKColors.Blue })
                recorder.BeginRecording(SKRect.Create(4, 4)).DrawRect(SKRect.Create(4, 4), blue);
            using SKPicture picture = recorder.EndRecording();
            using SKImage image = SKImage.FromPicture(picture, new SKSizeI(4, 4));

            var brush = new SolidColorBrush(Colors.Red) { Opacity = { Value = 50 } };
            using Brush.Resource resource = brush.ToResource(CompositionContext.Default);
            using var paint = new SKPaint();
            new BrushConstructor(new Rect(0, 0, 4, 4), resource, BlendMode.SrcOver, RenderIntent.Preview,
                drawableBrushMaterializer: null).ConfigurePaint(paint);

            Assert.Multiple(() =>
            {
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.DrawPicture(picture, paint);
                Assert.That(ReadCenter(surface), Is.EqualTo((0f, 0f, 0.5f, 0.5f)).Using<(float, float, float, float)>(Near),
                    "picture");

                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.DrawImage(image, 0, 0, paint);
                Assert.That(ReadCenter(surface), Is.EqualTo((0f, 0f, 0.5f, 0.5f)).Using<(float, float, float, float)>(Near),
                    "image");
            });
        });
    }

    // Premultiplied linear RGBA at the surface's center.
    private static (float R, float G, float B, float A) ReadCenter(SKSurface surface)
    {
        surface.Flush();
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF32, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
        float[] pixel = new float[4];
        GCHandle handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            Assert.That(surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 2, 2), Is.True);
        }
        finally
        {
            handle.Free();
        }

        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static bool Near((float, float, float, float) a, (float, float, float, float) b)
    {
        const float tolerance = 0.01f;
        return MathF.Abs(a.Item1 - b.Item1) < tolerance && MathF.Abs(a.Item2 - b.Item2) < tolerance
            && MathF.Abs(a.Item3 - b.Item3) < tolerance && MathF.Abs(a.Item4 - b.Item4) < tolerance;
    }
}

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics;

// On the GPU a paint's color reached the target as 8 bits in the target's color space, which in the linear working space
// rounded a dark color to a step of 1/255: sRGB 8, 13 and 17 all drew as 13 (#4288). Beutl's libSkiaSharp now sends colors
// to a float target as floats (native/SkiaSharp/float-target-vertex-colors.patch), and text, whose glyph vertices Skia keeps
// at 8 bits, gives its color through a shader.
[TestFixture]
[NonParallelizable]
public sealed class SolidColorPrecisionTests
{
    private static readonly byte[] s_grays = [4, 8, 13, 17, 22, 29, 40, 51, 64, 96, 128, 255];

    // Each kind reaches a different Skia draw: rectangles, ovals, rounded rectangles, convex paths, paths with a hole
    // (Beutl draws a stroke as a filled path), the same too large for Skia's path atlas, and glyphs.
    public enum Kind
    {
        Rectangle,
        Ellipse,
        RoundedRectangle,
        Triangle,
        Outline,
        LargeOutline,
        Text,
    }

    [Test]
    public void Dark_solid_colors_keep_their_sRGB_value_on_the_GPU([Values] Kind kind, [Values(255, 128)] int alpha)
    {
        int cell = kind == Kind.LargeOutline ? 320 : 48;
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene(cell * s_grays.Length, cell, "dark-colors");
            for (int i = 0; i < s_grays.Length; i++)
            {
                Drawable drawable = Create(kind, new SolidColorBrush(Color.FromArgb((byte)alpha, s_grays[i], s_grays[i], s_grays[i])));
                drawable.AlignmentX.Value = AlignmentX.Left;
                drawable.AlignmentY.Value = AlignmentY.Top;
                drawable.Transform.Value = new TranslateTransform((i * cell) + 12, 12);
                var element = new Element { ZIndex = { Value = i }, Length = { Value = MediaTime.FromSeconds(1) } };
                element.Objects.Add(drawable);
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
                    // The pixels a shape covers fully hold the fill's own color; antialiased edges are left out.
                    var channels = new List<int>();
                    for (int y = 0; y < cell; y++)
                    {
                        foreach (Bgra8888 pixel in srgb.GetRow<Bgra8888>(y).Slice(i * cell, cell))
                        {
                            if (pixel.A >= alpha - 1)
                                channels.AddRange([pixel.R, pixel.G, pixel.B]);
                        }
                    }

                    Assert.That(channels, Has.Count.GreaterThan(90), $"sRGB {s_grays[i]} covered too few pixels");
                    Assert.That(channels, Is.All.EqualTo(s_grays[i]).Within(1),
                        $"sRGB {s_grays[i]} drew as {(channels.Count == 0 ? "nothing" : $"{channels.Min()} to {channels.Max()}")}");
                }
            });
        });
    }

    private static Drawable Create(Kind kind, Brush fill)
    {
        switch (kind)
        {
            case Kind.Rectangle:
                return new RectShape { Width = { Value = 30 }, Height = { Value = 30 }, Fill = { Value = fill } };
            case Kind.Ellipse:
                return new EllipseShape { Width = { Value = 30 }, Height = { Value = 30 }, Fill = { Value = fill } };
            case Kind.RoundedRectangle:
                return new RoundedRectShape
                {
                    Width = { Value = 30 },
                    Height = { Value = 30 },
                    CornerRadius = { Value = new CornerRadius(10) },
                    Fill = { Value = fill },
                };
            case Kind.Triangle:
                var figure = new PathFigure();
                figure.StartPoint.Value = new Point(0, 0);
                figure.IsClosed.Value = true;
                figure.Segments.Add(new LineSegment(30, 0));
                figure.Segments.Add(new LineSegment(15, 30));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                return new GeometryShape { Data = { Value = geometry }, Fill = { Value = fill } };
            case Kind.Outline or Kind.LargeOutline:
                float size = kind == Kind.Outline ? 20 : 280;
                return new EllipseShape
                {
                    Width = { Value = size },
                    Height = { Value = size },
                    Fill = { Value = null },
                    Pen = { Value = new Pen { Brush = { Value = fill }, Thickness = { Value = 10 } } },
                };
            default:
                return new TextBlock
                {
                    Text = { Value = "I" },
                    Size = { Value = 32 },
                    FontWeight = { Value = FontWeight.Bold },
                    Fill = { Value = fill },
                };
        }
    }
}

using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Beutl.Graphics.UnitTests;

public class DeferradCanvasTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    private static void Draw(ICanvas canvas)
    {
        using (canvas.PushTransform(Matrix.CreateTranslation(100, 100)))
        {
            canvas.DrawRectangle(new Rect(0, 0, 500, 500), Brushes.White, null);

            using (canvas.PushTransform(Matrix.CreateTranslation(500, 500)))
            {
                canvas.DrawRectangle(new Rect(0, 0, 50, 50), Brushes.Blue, null);
            }

            canvas.DrawRectangle(new Rect(700, 0, 10, 10), Brushes.Red, null);
        }
    }

    [Test]
    public void Draw()
    {
        var container = new ContainerNode();
        var dcanvas = new DeferradCanvas(container);
        Draw(dcanvas);

        using var canvas = new ImmediateCanvas(1000, 700);
        using (canvas.Push())
        {
            container.Render(canvas);
            using Bitmap<Bgra8888> bmp = canvas.GetBitmap();
            Assert.That(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "canvas1.png"), EncodedImageFormat.Png));
        }

        using (canvas.Push())
        {
            canvas.Clear();
            Draw(canvas);
            using Bitmap<Bgra8888> bmp1 = canvas.GetBitmap();
            Assert.That(bmp1.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "canvas2.png"), EncodedImageFormat.Png));
        }
    }

    [Test]
    public void DrawEffect()
    {
        var container = new ContainerNode();
        var dcanvas = new DeferradCanvas(container);
        using (dcanvas.PushTransform(Matrix.CreateTranslation(75, 0)))
        using (dcanvas.PushTransform(Matrix.CreateRotation(MathF.PI / 4)))
        {
            using (dcanvas.PushFilterEffect(new Blur() { Sigma = new Size(10, 10) }))
            {
                dcanvas.DrawRectangle(new Rect(0, 0, 100, 100), Brushes.White, null);
            }

            dcanvas.DrawRectangle(new Rect(100, 100, 100, 100), Brushes.White, null);
        }

        using var canvas = new ImmediateCanvas(150, 150);
        using (canvas.Push())
        {
            container.Render(canvas);
            using Bitmap<Bgra8888> bmp = canvas.GetBitmap();
            Assert.That(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "direct.png"), EncodedImageFormat.Png));
        }

    }
}

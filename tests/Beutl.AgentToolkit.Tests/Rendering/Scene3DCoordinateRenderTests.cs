using System.Numerics;
using Beutl.AgentToolkit.Rendering;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

// Renders through the whole scene pipeline to check that 3D space lines up with the 2D canvas.
[NonParallelizable]
public sealed class Scene3DCoordinateRenderTests
{
    private const float RenderScale = 0.5f;

    private static TextBlock CreateText(float x, float y)
    {
        var block = new TextBlock();
        block.Text.CurrentValue = "Hello 3D";
        block.Size.CurrentValue = 120;
        block.Fill.CurrentValue = new SolidColorBrush(new Color(255, 230, 80, 20));
        block.Transform.CurrentValue = new TranslateTransform(x, y);
        return block;
    }

    private static RectShape CreateRect(float x, float y)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 400;
        rect.Height.CurrentValue = 250;
        rect.Fill.CurrentValue = new SolidColorBrush(new Color(160, 30, 120, 255));
        rect.Transform.CurrentValue = new TranslateTransform(x, y);
        return rect;
    }

    // An unmoved card shows its drawables exactly as a 2D render of them, edges and translucency included.
    [Test]
    public async Task UnmovedCards_MatchTheTwoDimensionalRender()
    {
        AgentToolkitGpuTestEnvironment.EnsureAvailable();

        float[] twoDimensional = await RenderAsync([CreateText(-300, -200), CreateRect(400, 150)]);
        float[] asCards = await RenderAsync(
        [
            CreateText(-300, -200), new DrawableObject3D(),
            CreateRect(400, 150), new DrawableObject3D(),
            new Scene3D()
        ]);

        Assert.That(asCards, Has.Length.EqualTo(twoDimensional.Length));
        float maximum = 0;
        for (int i = 0; i < asCards.Length; i++)
        {
            maximum = Math.Max(maximum, Math.Abs(asCards[i] - twoDimensional[i]));
        }

        Assert.That(maximum, Is.LessThanOrEqualTo(2f / 255f));
    }

    // A 200 px cube whose front face sits on z = 0 covers the same pixels as a 200 px 2D square, and a
    // negative Y places it above the center as it would in 2D.
    [Test]
    public async Task CubeFrontFaceOnTheZeroPlane_CoversItsTwoDimensionalFootprint()
    {
        AgentToolkitGpuTestEnvironment.EnsureAvailable();
        var cube = new Cube3D();
        cube.Position.CurrentValue = new Vector3(0, -200, 100);

        float[] pixels = await RenderAsync([cube, new DirectionalLight3D(), new Scene3D()]);

        (int left, int top, int right, int bottom) = OpaqueBounds(pixels, 1920 * RenderScale);
        // 2D footprint: x 860..1060, y 240..440 at full size. Seen from below, the cube's bottom face shows
        // under the front face, so only the bottom edge may reach further.
        Assert.That(new[] { left, top, right }, Is.EqualTo(new[] { 430, 120, 530 }));
        Assert.That(bottom, Is.GreaterThanOrEqualTo(220));
    }

    private static (int Left, int Top, int Right, int Bottom) OpaqueBounds(float[] pixels, float width)
    {
        int w = (int)width;
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int i = 0; i < pixels.Length / 4; i++)
        {
            if (pixels[(i * 4) + 3] < 0.5f)
                continue;
            int x = i % w;
            int y = i / w;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }

        return (left, top, right, bottom);
    }

    private static async Task<float[]> RenderAsync(EngineObject[] objects)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"beutl-3d-coordinates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var scene = new Scene(1920, 1080, "coordinates")
            {
                Duration = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(dir, "Scene.scene"))
            };
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(dir, "element.belm"))
            };
            foreach (EngineObject obj in objects)
            {
                element.AddObject(obj);
            }

            scene.Children.Add(element);
            using Bitmap bitmap = await new StillRenderer().RenderBitmapAsync(
                scene, TimeSpan.Zero, RenderScale, CancellationToken.None);
            Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            return bitmap.GetPixelSpan<Half>().ToArray().Select(static v => (float)v).ToArray();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

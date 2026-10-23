using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Renders one drawable through a real Scene and SceneRenderer, the path a project frame takes.
internal static class SceneRenderHarness
{
    /// <summary>
    /// Renders <paramref name="drawable"/> as the only element of a <paramref name="frame"/>-sized scene at
    /// <paramref name="time"/>, with the render cache off. Each call gets its own scene name and URIs.
    /// </summary>
    public static Bitmap RenderSingle(
        Drawable drawable,
        PixelSize frame,
        float outputScale,
        float maxWorkingScale,
        MediaTime time)
    {
        string id = Guid.NewGuid().ToString("N");
        var scene = new Scene(frame.Width, frame.Height, id) { Uri = new Uri($"file:///{id}/scene") };
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(60) },
            ZIndex = { Value = 0 },
            IsEnabled = { Value = true },
            Uri = new Uri($"file:///{id}/element"),
        };
        element.AddObject(drawable);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, RenderIntent.Preview, outputScale, false, maxWorkingScale)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(renderer.Compositor.EvaluateGraphics(time));
        return renderer.Snapshot();
    }

    /// <summary>Sums the alpha of every pixel.</summary>
    public static double SumAlpha(Bitmap bitmap)
    {
        double sum = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 3; x < row.Length; x += 4)
                sum += (float)BitConverter.UInt16BitsToHalf(row[x]);
        }

        return sum;
    }
}

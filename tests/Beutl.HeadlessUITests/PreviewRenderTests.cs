using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// GPU-gated: renders a real preview frame through SceneRenderer. Skips cleanly when no
// Vulkan/MoltenVK/SwiftShader device is available; runs (e.g. on macOS/MoltenVK) when one is.
[TestFixture]
public class PreviewRenderTests
{
    private static async Task<Scene> NewSceneWithRectangle(string name)
    {
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name, 320, 240);
        await TestShell.AddElementAsync(editor, () => new RectShape
        {
            Width = { Value = 200 },
            Height = { Value = 150 },
            Fill = { Value = new SolidColorBrush(Colors.Red) }
        }, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        return editor.Scene;
    }

    [AvaloniaTest]
    public async Task SceneRenderer_renders_a_non_empty_preview_frame()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();

        Scene scene = await NewSceneWithRectangle("preview");

        Bitmap snapshot = RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            CompositionFrame frame = renderer.Compositor.EvaluateGraphics(TimeSpan.Zero);
            renderer.Render(frame);
            return renderer.Snapshot();
        });

        using (snapshot)
        {
            Assert.That(snapshot.Width, Is.EqualTo(320));
            Assert.That(snapshot.Height, Is.EqualTo(240));
            AssertContainsRedPixel(snapshot);
        }
    }

    [AvaloniaTest]
    public async Task SceneRenderer_renders_through_the_player_view_model()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();

        Scene scene = await NewSceneWithRectangle("playerpreview");
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        Bitmap snapshot = RenderThread.Dispatcher.Invoke(() =>
        {
            SceneRenderer renderer = editor.Renderer.Value;
            CompositionFrame frame = renderer.Compositor.EvaluateGraphics(TimeSpan.Zero);
            renderer.Render(frame);
            return renderer.Snapshot();
        });

        using (snapshot)
        {
            Assert.That(snapshot.Width, Is.GreaterThan(0));
            Assert.That(snapshot.Height, Is.GreaterThan(0));
            AssertContainsRedPixel(snapshot);
        }
    }

    // The scene holds only an opaque red rectangle, so a red pixel proves the frame was drawn.
    // The surface is RgbaF16, so convert to BGRA first.
    private static void AssertContainsRedPixel(Bitmap snapshot)
    {
        using Bitmap bgra = snapshot.Convert(BitmapColorType.Bgra8888);
        foreach (Bgra8888 p in bgra.GetPixelSpan<Bgra8888>())
        {
            if (p.A > 0 && p.R > 200 && p.G < 80 && p.B < 80)
                return;
        }

        Assert.Fail("Rendered frame contains no red pixel; SceneRenderer produced a blank/transparent frame.");
    }
}

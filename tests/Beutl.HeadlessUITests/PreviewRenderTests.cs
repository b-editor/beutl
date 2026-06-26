using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// GPU-gated (B2): renders a real preview frame through SceneRenderer. Skips cleanly when no
// Vulkan/MoltenVK/SwiftShader device is available; runs (e.g. on macOS/MoltenVK) when one is.
[TestFixture]
public class PreviewRenderTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<Scene> NewSceneWithRectangle(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new RectShape
            {
                Width = { CurrentValue = 200 },
                Height = { CurrentValue = 150 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
            }));
        HeadlessTestHelpers.Settle();
        return scene;
    }

    [AvaloniaTest]
    public async Task SceneRenderer_renders_a_non_empty_preview_frame()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();

        Scene scene = await NewSceneWithRectangle("preview");

        Bitmap snapshot = RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new SceneRenderer(scene);
            CompositionFrame frame = renderer.Compositor.EvaluateGraphics(TimeSpan.Zero);
            renderer.Render(frame);
            return renderer.Snapshot();
        });

        using (snapshot)
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Width, Is.EqualTo(320));
            Assert.That(snapshot.Height, Is.EqualTo(240));
            AssertContainsRedPixel(snapshot);
        }
    }

    [AvaloniaTest]
    public async Task SceneRenderer_renders_through_the_player_view_model()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();

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

    // ByteCount > 0 only proves a buffer was allocated; the scene's sole content is an opaque red
    // rectangle, so a blank/transparent frame (broken rendering) is caught only by finding its pixels.
    // The render surface is RgbaF16, so convert to 8-bit BGRA before sampling.
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

using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// GPU-gated (B2): renders a real preview frame through SceneRenderer. Skips cleanly when no
// Vulkan/MoltenVK/SwiftShader device is available; runs (e.g. on macOS/MoltenVK) when one is.
public class PreviewRenderTests
{
    private static void ResetProject() => TestReset.ResetShell();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<Scene> NewSceneWithRectangle(string name)
    {
        Project project = (await ProjectService.Current.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;

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
        ResetProject();

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
            Assert.That(snapshot.ByteCount, Is.GreaterThan(0));
        }
    }

    [AvaloniaTest]
    public async Task SceneRenderer_renders_through_the_player_view_model()
    {
        GpuTestGate.EnsureAvailable();
        ResetProject();

        Scene scene = await NewSceneWithRectangle("playerpreview");
        var editor = (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;

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
            Assert.That(snapshot.ByteCount, Is.GreaterThan(0));
        }
    }
}

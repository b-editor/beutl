using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// While the player's path editor edits a shape, the frame shows the shape with the editor's own points alone: the white
// boundary drawn into the frame and the blue box of the transform handles would only cover them.
[NonParallelizable]
[TestFixture]
public sealed class PlayerViewPathEditingTests
{
    [AvaloniaTest]
    public async Task The_boundary_and_the_transform_handles_give_way_to_the_path_editor()
    {
        TestGpu.RequireAnyDevice();
        using PathEditorInteractionTests.Fixture fixture = await PathEditorInteractionTests.Fixture.Create();
        EditViewModel editor = fixture.Editor;
        PlayerViewModel player = editor.Player;
        Element element = editor.Scene.Children.Single();
        element.Objects.OfType<GeometryShape>().Single().Fill.Value = new SolidColorBrush(Colors.Red);
        // Selecting the element opens its properties, which keep their view state beside its file.
        element.Uri = new Uri(Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "path.belm"));
        editor.GetRequiredService<IEditorSelection>().Select(element);
        player.IsMoveMode.Value = true;

        var view = new PlayerView { DataContext = player };
        fixture.Window.Content = view;
        try
        {
            await RenderedAfter(player, player.QueuePreviewRender);
            Assert.Multiple(() =>
            {
                Assert.That(WhitePixels(player), Is.GreaterThan(0), "the selection's boundary");
                Assert.That(view.transformHandlesOverlay.IsVisible, Is.True);
                Assert.That(view.transformHandlesOverlay.Drawable, Is.Not.Null, "the selection's handles");
            });

            // Opening and closing the editor draws the paused frame again by itself.
            await RenderedAfter(player, () => player.PathEditor.FigureContext.Value = fixture.Model.FigureContext.Value);
            Assert.Multiple(() =>
            {
                Assert.That(player.PathEditor.IsVisible.Value, Is.True);
                Assert.That(WhitePixels(player), Is.Zero, "no boundary while the path is edited");
                Assert.That(view.transformHandlesOverlay.IsVisible, Is.False);
                Assert.That(view.transformHandlesOverlay.Drawable, Is.Null, "no handles while the path is edited");
            });

            await RenderedAfter(player, () => player.PathEditor.FigureContext.Value = null);
            Assert.Multiple(() =>
            {
                Assert.That(WhitePixels(player), Is.GreaterThan(0), "the boundary comes back");
                Assert.That(view.transformHandlesOverlay.IsVisible, Is.True);
                Assert.That(view.transformHandlesOverlay.Drawable, Is.Not.Null, "and the handles");
            });
        }
        finally
        {
            view.DataContext = null;
            player.PathEditor.FigureContext.Value = null;
        }
    }

    // Waits for the preview to render after the change, without asking for a render itself.
    private static async Task RenderedAfter(PlayerViewModel player, Action change)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = player.AfterRendered
            .Take(1)
            .Subscribe(_ => rendered.TrySetResult());
        change();
        HeadlessTestHelpers.Render();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render(3);
    }

    // The boundary is the only white the frame holds: the shape is red on a transparent scene.
    private static int WhitePixels(PlayerViewModel player)
    {
        using Ref<Bitmap> frame = player.PreviewImage.Value!.Clone();
        using Bitmap bgra = frame.Value.Convert(BitmapColorType.Bgra8888);
        int white = 0;
        foreach (Bgra8888 pixel in bgra.GetPixelSpan<Bgra8888>())
        {
            if (pixel.A > 0 && pixel.R > 240 && pixel.G > 240 && pixel.B > 240)
                white++;
        }

        return white;
    }
}

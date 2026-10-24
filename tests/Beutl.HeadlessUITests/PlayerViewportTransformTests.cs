using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;
using Beutl.Views;
using PixelSize = Beutl.Media.PixelSize;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlayerViewportTransformTests
{
    [TestCase(1920, 1080, 480, 270)]
    [TestCase(1080, 1920, 270, 480)]
    [TestCase(640, 480, 960, 720)]
    public void Uniform_fit_round_trips_frame_points_without_clamping(int width, int height, int previewWidth, int previewHeight)
    {
        var viewport = PlayerViewportTransform.Create(new Size(previewWidth, previewHeight), new PixelSize(width, height))!;
        Assert.That(viewport.ToFrame(new Point(previewWidth / 2d, previewHeight / 2d)), Is.EqualTo(new Point(width / 2d, height / 2d)));
        foreach (Point frame in new[] { new Point(-12, 30), new Point(width, height), new Point(width + 50, height + 90) })
        {
            Point actual = viewport.ToFrame(viewport.ToPreview(frame));
            Assert.That(actual.X, Is.EqualTo(frame.X).Within(1e-8));
            Assert.That(actual.Y, Is.EqualTo(frame.Y).Within(1e-8));
        }
    }

    [TestCase(0, 270, 1920, 1080)]
    [TestCase(480, 0, 1920, 1080)]
    [TestCase(480, 270, 0, 1080)]
    [TestCase(480, 270, 1920, 0)]
    [TestCase(double.NaN, 270, 1920, 1080)]
    [TestCase(double.PositiveInfinity, 270, 1920, 1080)]
    public void Invalid_layout_has_no_mapping(double width, double height, int frameWidth, int frameHeight)
    {
        Assert.That(PlayerViewportTransform.Create(new Size(width, height), new PixelSize(frameWidth, frameHeight)), Is.Null);
    }

    [AvaloniaTest]
    public async Task Player_reuses_the_mapping_until_layout_or_frame_size_changes_and_shares_it_with_the_path_overlay()
    {
        await TestReset.ResetShellAsync();
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("viewport-transform");
        scene.FrameSize.Value = new PixelSize(640, 480);
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(4));
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
            editor.Player.QueuePreviewRender();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(3);
            PlayerViewportTransform first = view.ViewportTransform!;
            Assert.That(first, Is.Not.Null);
            Assert.That(view.ViewportTransform, Is.SameAs(first));
            var path = view.FindControl<PathEditorView>("pathEditorView")!;
            Assert.That(path.Scale, Is.EqualTo(first.FrameScale));

            editor.Player.FrameMatrix.Value = Beutl.Graphics.Matrix.CreateScale(1.5f, 1.5f) * Beutl.Graphics.Matrix.CreateTranslation(27, -14);
            HeadlessTestHelpers.Render(3);
            Point imagePoint = first.ToPreview(new Point(100, 200));
            Point windowPoint = view.image.TranslatePoint(imagePoint, window)!.Value;
            Point restored = first.ToFrame(window.TranslatePoint(windowPoint, view.image)!.Value);
            Assert.That(restored.X, Is.EqualTo(100).Within(1e-5));
            Assert.That(restored.Y, Is.EqualTo(200).Within(1e-5));
            Assert.That(view.ViewportTransform, Is.SameAs(first), "Avalonia removes pan/zoom before the frame mapping.");

            scene.FrameSize.Value = new PixelSize(1280, 960);
            HeadlessTestHelpers.Settle();
            PlayerViewportTransform resized = view.ViewportTransform!;
            Assert.That(resized, Is.Not.SameAs(first));
            Assert.That(resized.FrameSize, Is.EqualTo(scene.FrameSize.Value));
            Assert.That(path.Scale, Is.EqualTo(resized.FrameScale));
            window.Width = 1000;
            window.Height = 750;
            HeadlessTestHelpers.Render(3);
            Assert.That(view.ViewportTransform, Is.Not.SameAs(resized));
            Assert.That(path.Scale, Is.EqualTo(view.ViewportTransform!.FrameScale));
            view.DataContext = null;
            Assert.That(view.ViewportTransform, Is.Null);
        }
        finally { window.Close(); }
    }
}

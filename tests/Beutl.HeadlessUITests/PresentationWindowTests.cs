using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class PresentationWindowTests
{
    [AvaloniaTest]
    public async Task Monitor_output_has_no_native_owner_but_closes_with_the_editor_window()
    {
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("presentation-window-lifetime");
        // No PlayerView in the owner: its detachment must not mask a missing lifetime subscription.
        var owner = new Window { Width = 640, Height = 480 };
        var output = new PresentationWindow(editor.Player);
        try
        {
            owner.Show();
            output.ShowOnScreen(owner, owner.Screens.Primary, true);
            Assert.That(output.Owner, Is.Null, "An owned window cannot independently enter fullscreen on macOS.");
            Assert.That(output.WindowState, Is.EqualTo(WindowState.FullScreen));
            owner.Close();
            Assert.That(output.IsVisible, Is.False);
            Assert.That(editor.Player.CleanPreviewImage.Value, Is.Null);
        }
        finally
        {
            output.Close();
            owner.Close();
        }
    }

    [AvaloniaTest]
    public async Task Switching_tabs_closes_output_but_activating_its_owner_does_not()
    {
        var (editor, view, owner) = await OpenView();
        try
        {
            var sourceTab = TestShell.Editor.SelectedTabItem.Value!;
            var secondScene = new Scene(320, 240, "Second")
            {
                Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "Second.scene"))
            };
            CoreSerializer.StoreToUri(secondScene, secondScene.Uri);
            TestShell.Editor.ActivateTabItem(secondScene);
            var secondTab = TestShell.Editor.SelectedTabItem.Value!;
            TestShell.Editor.SelectedTabItem.Value = sourceTab;

            view.OpenPresentation(null, false);
            PresentationWindow output = view.Presentation!;
            owner.Activate();
            HeadlessTestHelpers.Settle();
            Assert.That(output.IsVisible, Is.True, "Window focus changes are not editor tab changes.");
            view.OpenPresentation(null, false);
            Assert.That(view.Presentation, Is.SameAs(output));

            TestShell.Editor.SelectedTabItem.Value = secondTab;
            Assert.That(output.IsVisible, Is.False);
            Assert.That(view.Presentation, Is.Null);
            Assert.That(editor.Player.CleanPreviewImage.Value, Is.Null);

            TestShell.Editor.SelectedTabItem.Value = sourceTab;
            Assert.That(view.Presentation, Is.Null, "Returning to a tab must not reopen its output.");
            view.OpenPresentation(null, false);
            Assert.That(view.Presentation!.IsVisible, Is.True);
            await TestShell.Editor.CloseTabItem(sourceTab, saveChanges: false);
            Assert.That(view.Presentation, Is.Null);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTest]
    public async Task Output_keyboard_controls_the_shared_player_and_closing_does_not_pause()
    {
        var (editor, view, owner) = await OpenView();
        try
        {
            view.OpenPresentation(null, false);
            PresentationWindow output = view.Presentation!;
            MediaTime before = editor.Player.CurrentFrame.Value;
            Press(output, Key.Right);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.CurrentFrame.Value > before);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(before + MediaTime.FromFrames(1, editor.Player.GetFrameRate())));
            Press(output, Key.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.CurrentFrame.Value == before);

            Press(output, Key.F11);
            Assert.That(output.WindowState, Is.EqualTo(WindowState.FullScreen));
            Press(output, Key.F11);
            Assert.That(output.WindowState, Is.EqualTo(WindowState.Normal));

            // Isolate the close action from native audio/device timing.
            editor.Player.IsPlaying.Value = true;
            Press(output, Key.Escape);
            Assert.That(view.Presentation, Is.Null);
            Assert.That(editor.Player.IsPlaying.Value, Is.True);
        }
        finally
        {
            editor.Player.IsPlaying.Value = false;
            owner.Close();
        }
    }

    [AvaloniaTest]
    public async Task Playback_shares_the_current_frame_and_frame_step_pauses_the_shared_player()
    {
        TestGpu.RequireAnyDevice();
        var (editor, view, owner) = await OpenView();
        try
        {
            view.OpenPresentation(null, false);
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
            PresentationWindow output = view.Presentation!;
            Press(output, Key.Space);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.IsPlaying.Value
                && editor.Player.CleanPreviewImage.Value is { } clean
                && ReferenceEquals(clean.Value, editor.Player.PreviewImage.Value?.Value));

            Press(output, Key.Right);
            await HeadlessTestHelpers.WaitUntilAsync(() => !editor.Player.IsPlaybackActive);
            Assert.That(output.IsVisible, Is.True);
            Assert.That(editor.Player.IsPlaying.Value, Is.False);
        }
        finally
        {
            await editor.Player.Pause();
            owner.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Output_omits_boundaries_and_onion_skin_without_removing_them_from_the_editor(bool cache, bool onion)
    {
        TestGpu.RequireAnyDevice();
        var config = GlobalConfiguration.Instance.EditorConfig;
        bool oldOnion = config.IsOnionSkinEnabled.Value;
        int oldPrevCount = config.OnionSkinPrevCount.Value;
        int oldNextCount = config.OnionSkinNextCount.Value;
        float oldOpacity = config.OnionSkinPrevOpacity.Value;
        bool oldHdr = config.UseHdrPreview.Value;
        var (editor, view, owner) = await OpenView();
        try
        {
            config.UseHdrPreview.Value = false;
            config.IsOnionSkinEnabled.Value = onion;
            config.OnionSkinPrevCount.Value = 1;
            config.OnionSkinNextCount.Value = 0;
            config.OnionSkinPrevOpacity.Value = 0.5f;
            MediaTime now = MediaTime.FromSeconds(1);
            MediaTime frame = MediaTime.FromFrames(1, editor.Player.GetFrameRate());
            await TestShell.AddElementAsync(editor, () => new RectShape
            {
                Width = { Value = 120 },
                Height = { Value = 100 },
                Fill = { Value = new SolidColorBrush(Colors.Blue) }
            }, now - frame, frame);
            await TestShell.AddElementAsync(editor, () => new RectShape
            {
                Width = { Value = 60 },
                Height = { Value = 50 },
                Fill = { Value = new SolidColorBrush(Colors.Red) }
            }, now, frame);
            editor.GetRequiredService<IEditorSelection>().Select(editor.Scene.Children.Last());
            editor.Player.ApplyTimecodeSeek(now);
            view.OpenPresentation(null, false);
            editor.FrameCacheManager.Value.IsEnabled = cache;
            editor.FrameCacheManager.Value.Clear();

            // The second pass exercises a cache hit when enabled. Both passes must remain clean.
            for (int pass = 0; pass < 2; pass++)
            {
                editor.Player.QueuePreviewRender();
                RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
                using var clean = editor.Player.CleanPreviewImage.Value!.Clone();
                using var decorated = editor.Player.PreviewImage.Value!.Clone();
                var cleanPixels = CountPixels(clean.Value);
                var editorPixels = CountPixels(decorated.Value);
                Assert.Multiple(() =>
                {
                    Assert.That(cleanPixels.Red, Is.GreaterThan(0));
                    Assert.That(cleanPixels.White, Is.Zero);
                    Assert.That(cleanPixels.Blue, Is.Zero);
                    Assert.That(editorPixels.White, Is.GreaterThan(0));
                    if (onion) Assert.That(editorPixels.Blue, Is.GreaterThan(0));
                });
            }

            HeadlessTestHelpers.Render();
            using var screenshot = view.Presentation!.CaptureRenderedFrame();
            Assert.That(screenshot, Is.Not.Null);
            Assert.That(HeadlessTestHelpers.ReadPixel(screenshot!, new Avalonia.PixelPoint(1, 1)), Is.EqualTo((0, 0, 0)));
            HeadlessCapture.SaveIfRequested(owner, nameof(PresentationWindowTests), $"editor-{cache}-{onion}");
            HeadlessCapture.SaveIfRequested(view.Presentation!, nameof(PresentationWindowTests), $"output-{cache}-{onion}");

            view.Presentation!.Close();
            editor.Player.QueuePreviewRender();
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
            Assert.That(editor.Player.CleanPreviewImage.Value, Is.Null);
        }
        finally
        {
            owner.Close();
            config.UseHdrPreview.Value = oldHdr;
            config.IsOnionSkinEnabled.Value = oldOnion;
            config.OnionSkinPrevCount.Value = oldPrevCount;
            config.OnionSkinNextCount.Value = oldNextCount;
            config.OnionSkinPrevOpacity.Value = oldOpacity;
        }
    }

    [AvaloniaTest]
    public async Task Output_size_is_shared_and_changes_during_playback_are_applied_after_pause()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("presentation-size", 320, 240)).Editor;
        try
        {
            editor.Player.MaxFrameSize = new Size(80, 60);
            editor.PreviewScale.Value = RenderScale.FitToPreviewer;
            editor.Player.SetPresentationFrameSize(new Size(240, 180));
            Assert.That(editor.PreviewSurfaceSize.Value, Is.EqualTo(new Size(240, 180)));
            var renderer = editor.Renderer.Value;

            editor.Player.IsPlaying.Value = true;
            editor.Player.SetPresentationFrameSize(new Size(320, 240));
            Assert.That(editor.Renderer.Value, Is.SameAs(renderer));
            await editor.Player.Pause();
            HeadlessTestHelpers.Settle();
            Assert.That(editor.PreviewSurfaceSize.Value, Is.EqualTo(new Size(320, 240)));
            editor.Player.SetPresentationFrameSize(default);
            Assert.That(editor.PreviewSurfaceSize.Value, Is.EqualTo(new Size(80, 60)));
            Assert.That(editor.PreviewScale.Value, Is.EqualTo(RenderScale.FitToPreviewer));
        }
        finally { await editor.Player.Pause(); }
    }

    private static async Task<(EditViewModel Editor, PlayerView View, Window Owner)> OpenView()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("presentation", 320, 240)).Editor;
        var view = new PlayerView { DataContext = editor.Player };
        var owner = new Window { Content = view, Width = 640, Height = 480 };
        owner.Show();
        HeadlessTestHelpers.Render();
        return (editor, view, owner);
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        if (window.IsVisible)
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Settle();
    }

    private static (int Red, int Blue, int White) CountPixels(Bitmap bitmap)
    {
        using var bgra = bitmap.Convert(BitmapColorType.Bgra8888);
        int red = 0, blue = 0, white = 0;
        foreach (Bgra8888 pixel in bgra.GetPixelSpan<Bgra8888>())
        {
            if (pixel.A == 0) continue;
            if (pixel.R > 200 && pixel.G < 60 && pixel.B < 60) red++;
            if (pixel.B > 100 && pixel.G < 60 && pixel.R < 60) blue++;
            if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) white++;
        }
        return (red, blue, white);
    }
}

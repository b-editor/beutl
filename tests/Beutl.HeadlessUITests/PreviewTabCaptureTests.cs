using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Beutl.Controls.Styling.Themes;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class PreviewTabCaptureTests
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("BEUTL_PREVIEW_CAPTURE_DIR")
        ?? Path.Combine(Path.GetTempPath(), "beutl-preview-captures");

    private static void Save(Window window, string name)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        Directory.CreateDirectory(OutputDirectory);
        frame!.Save(Path.Combine(OutputDirectory, name), PngBitmapEncoderOptions.Default);
    }

    // The frame's size beside each capture, to compare what a layout change gives the frame.
    private static void RecordSizes(PlayerView view, string name)
    {
        Control footer = view.Player.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_FooterPanel");
        Control seekBar = view.Player.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Slider");
        Directory.CreateDirectory(OutputDirectory);
        File.AppendAllText(Path.Combine(OutputDirectory, "sizes.txt"),
            $"{name}: player {view.Bounds.Width:0}x{view.Bounds.Height:0}, frame {view.image.Bounds.Width:0.#}x{view.image.Bounds.Height:0.#}, "
            + $"seek bar height {seekBar.Bounds.Height:0.#}, footer {footer.Bounds.Width:0}x{footer.Bounds.Height:0.#}, "
            + $"below frame {view.Bounds.Height - Avalonia.VisualExtensions.TranslatePoint(view.image, new Avalonia.Point(0, view.image.Bounds.Height), view)!.Value.Y:0.#}\n");
    }

    private static async Task<EditViewModel> OpenSampleScene(ThemeVariant? theme = null)
    {
        await TestReset.ResetShellAsync();
        if (theme == ThemeVariant.Light)
            ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Light, null);
        else
            ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);

        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("previewcapture");
        editor.Scene.FrameSize.Value = new PixelSize(1920, 1080);
        editor.Scene.Duration.Value = MediaTime.FromSeconds(300);

        var rect = new RectShape();
        rect.Width.Value = 800;
        rect.Height.Value = 500;
        rect.Fill.Value = new SolidColorBrush(0xFF3B82F6);
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            MediaTime.Zero, MediaTime.FromSeconds(8), 0, new ElementSource.EngineObject(() => rect))], CancellationToken.None);
        HeadlessTestHelpers.Settle();
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(2.5));
        HeadlessTestHelpers.Settle();
        return editor;
    }

    private static async Task Prepare(Window window)
    {
        window.Show();
        HeadlessTestHelpers.Render(3);
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render(5);
    }

    [AvaloniaTest]
    public async Task Capture_shell()
    {
        EditViewModel editor = await OpenSampleScene();
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1600, Height = 1000 };
        try
        {
            await Prepare(window);
            Save(window, "shell.png");
            RecordSizes(window.GetVisualDescendants().OfType<PlayerView>().Single(), "shell");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(1200, 520, "short")]
    [TestCase(960, 620, "compare")]
    [TestCase(300, 300, "compare-300")]
    [TestCase(1100, 660, "wide")]
    [TestCase(760, 520, "medium")]
    [TestCase(420, 420, "narrow")]
    [TestCase(280, 300, "tiny")]
    [TestCase(200, 280, "smallest")]
    public async Task Capture_player(int width, int height, string name)
    {
        EditViewModel editor = await OpenSampleScene();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            await Prepare(window);
            Save(window, $"player-{name}.png");
            RecordSizes(view, name);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The time editor takes more of the footer than the time does.
    [AvaloniaTest]
    [TestCase(1200, 520, "wide")]
    [TestCase(560, 420, "medium")]
    [TestCase(300, 320, "narrow")]
    [TestCase(240, 320, "narrowest")]
    public async Task Capture_player_editing_the_time(int width, int height, string name)
    {
        EditViewModel editor = await OpenSampleScene();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            await Prepare(window);
            view.Player.BeginEditCurrentTime();
            HeadlessTestHelpers.Render(4);
            Save(window, $"editing-{name}.png");
            RecordSizes(view, $"editing-{name}");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Capture_player_light()
    {
        EditViewModel editor = await OpenSampleScene(ThemeVariant.Light);
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 960, Height = 600 };
        try
        {
            await Prepare(window);
            Save(window, "player-light.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        }
    }

    [AvaloniaTest]
    public async Task Capture_player_states()
    {
        EditViewModel editor = await OpenSampleScene();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 960, Height = 600 };
        bool guides = player.IsPreviewGuideEnabled.Value;
        try
        {
            await Prepare(window);

            player.IsPreviewGuideEnabled.Value = true;
            player.IsLoopEnabled.Value = true;
            player.PlaybackDirection.Value = PlaybackDirection.Backward;
            player.PlaybackSpeed.Value = 4f;
            HeadlessTestHelpers.Render(4);
            Save(window, "state-shuttle-guides-loop.png");
            player.IsPreviewGuideEnabled.Value = guides;
            player.IsLoopEnabled.Value = false;
            player.PlaybackDirection.Value = PlaybackDirection.Stopped;
            player.PlaybackSpeed.Value = 1f;
            HeadlessTestHelpers.Render(2);

            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            HeadlessTestHelpers.Render(4);
            Save(window, "state-zoomed.png");
            player.RequestPreviewZoom(PreviewZoomAction.ZoomOut);
            player.RequestPreviewZoom(PreviewZoomAction.ZoomOut);
            player.RequestPreviewZoom(PreviewZoomAction.ZoomOut);
            HeadlessTestHelpers.Render(4);
            Save(window, "state-zoomed-out.png");
            player.RequestPreviewZoom(PreviewZoomAction.Fit);
            HeadlessTestHelpers.Render(2);

            view.zoomComboBox.IsDropDownOpen = true;
            HeadlessTestHelpers.Render(4);
            Save(window, "state-zoom-menu.png");
        }
        finally
        {
            player.IsPreviewGuideEnabled.Value = guides;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Capture_player_resolution_menu()
    {
        EditViewModel editor = await OpenSampleScene();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 960, Height = 600 };
        try
        {
            await Prepare(window);
            view.resolutionComboBox.IsDropDownOpen = true;
            HeadlessTestHelpers.Render(4);
            Save(window, "state-resolution-menu.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}

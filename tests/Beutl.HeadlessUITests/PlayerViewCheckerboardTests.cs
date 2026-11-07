using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Configuration;
using Beutl.Graphics.Rendering;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// The preview can show a checkerboard behind the frame in place of black, so that transparent parts stand apart from
// black ones. Its squares keep their size on screen whatever the zoom (#4066).
[NonParallelizable]
[TestFixture]
public sealed class PlayerViewCheckerboardTests
{
    private const int Square = 8;
    private const int Light = 153;
    private const int Dark = 102;

    private (bool Hdr, bool Checkerboard, bool Guides, bool OnionSkin) _saved;

    private static EditorConfig Config => GlobalConfiguration.Instance.EditorConfig;

    // Nothing else is drawn over the frame.
    [SetUp]
    public void SetUp()
    {
        _saved = (Config.UseHdrPreview.Value, Config.IsPreviewCheckerboardEnabled.Value, Config.IsPreviewGuideEnabled.Value,
            Config.IsOnionSkinEnabled.Value);
        Config.IsPreviewGuideEnabled.Value = false;
        Config.IsOnionSkinEnabled.Value = false;
    }

    [TearDown]
    public void TearDown()
    {
        Config.UseHdrPreview.Value = _saved.Hdr;
        Config.IsPreviewCheckerboardEnabled.Value = _saved.Checkerboard;
        Config.IsPreviewGuideEnabled.Value = _saved.Guides;
        Config.IsOnionSkinEnabled.Value = _saved.OnionSkin;
    }

    // A 1920 x 1080 frame with nothing in it, so all of it is transparent.
    internal static async Task<(EditViewModel Editor, PlayerView View, Window Window)> Open(bool hdr)
    {
        Config.UseHdrPreview.Value = hdr;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-checkerboard", 1920, 1080);
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0), "the preview has a frame");
        return (editor, view, window);
    }

    [AvaloniaTest]
    public async Task A_transparent_frame_shows_squares_of_one_size_at_any_zoom_and_black_without_them()
    {
        Config.IsPreviewCheckerboardEnabled.Value = false;
        (_, PlayerView view, Window window) = await Open(hdr: false);
        try
        {
            int[] black = ReadSquares(view, window);

            Config.IsPreviewCheckerboardEnabled.Value = true;
            HeadlessTestHelpers.Render();
            int[] atFit = ReadSquares(view, window);

            view.zoomComboBox.SelectedItem = view.zoomComboBox.Items.Cast<PreviewZoomOption>()
                .Single(option => option.Level == 4);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            int[] atFour = ReadSquares(view, window);

            Assert.Multiple(() =>
            {
                Assert.That(black, Is.All.InRange(0, 3), "black without the checkerboard");
                Assert.That(atFit, Is.EqualTo(Expected).Within(3), "at fit");
                Assert.That(atFour, Is.EqualTo(Expected).Within(3), "at 400%");
            });
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task The_HDR_preview_shows_the_checkerboard_as_the_setting_says()
    {
        TestGpu.RequireAnyDevice();
        Config.IsPreviewCheckerboardEnabled.Value = true;
        (_, PlayerView view, Window window) = await Open(hdr: true);
        try
        {
            bool shown = view.hdrView!.ShowsCheckerboard;
            Config.IsPreviewCheckerboardEnabled.Value = false;
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(shown, Is.True);
                Assert.That(view.hdrView!.ShowsCheckerboard, Is.False);
            });
        }
        finally
        {
            Close(window);
        }
    }

    // Turns the checkerboard off on the UI thread and lets the player take that in before its window closes. Left on, the
    // player's copy of the setting wrote it back after TearDown, into the tests after this one.
    private static void Close(Window window)
    {
        Config.IsPreviewCheckerboardEnabled.Value = false;
        HeadlessTestHelpers.Settle();
        window.Close();
        HeadlessTestHelpers.Settle();
    }

    // The middles of three squares in a row and the one under the first, counted from the frame's top left corner, so the
    // first is light: light, dark, light, and dark under it.
    private static readonly int[] Expected = [Light, Dark, Light, Dark];

    // Reads the middles of the squares around the middle of the panel: the square the middle falls in, if it is light,
    // or the one after it, then the next two along and the one under it.
    private static int[] ReadSquares(PlayerView view, Window window)
    {
        Point origin = view.image.TranslatePoint(default, window)!.Value;
        Point middle = view.previewViewport.TranslatePoint(
            new Point(view.previewViewport.Bounds.Width / 2, view.previewViewport.Bounds.Height / 2), window)!.Value;
        int column = (int)Math.Floor((middle.X - origin.X) / Square);
        int row = (int)Math.Floor((middle.Y - origin.Y) / Square);
        if ((column + row) % 2 != 0)
            column++;

        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        int Red(int c, int r)
        {
            int x = (int)Math.Floor(origin.X + (c * Square) + (Square / 2));
            int y = (int)Math.Floor(origin.Y + (r * Square) + (Square / 2));
            return pixels[(y * buffer.RowBytes) + (x * 4) + (buffer.Format == PixelFormat.Bgra8888 ? 2 : 0)];
        }

        return [Red(column, row), Red(column + 1, row), Red(column + 2, row), Red(column, row + 1)];
    }
}

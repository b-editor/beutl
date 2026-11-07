using System.Reactive.Linq;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode;
using BtlMatrix = Beutl.Graphics.Matrix;

namespace Beutl.HeadlessUITests;

// The preview draws the frame smoothly, and each of its pixels as a sharp square once a zoom the user chose shows it two
// device pixels wide or more (#4082).
[NonParallelizable]
[TestFixture]
public sealed class PlayerViewPreviewSamplingTests
{
    private bool _savedHdr;

    private static EditorConfig Config => GlobalConfiguration.Instance.EditorConfig;

    [SetUp]
    public void SetUp() => _savedHdr = Config.UseHdrPreview.Value;

    [TearDown]
    public void TearDown() => Config.UseHdrPreview.Value = _savedHdr;

    private static async Task<(EditViewModel Editor, PlayerView View, Window Window)> Open(bool hdr, int width, int height)
    {
        Config.UseHdrPreview.Value = hdr;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-sampling", width, height);
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

    // Picks a level in the zoom box, or fit for null.
    private static void ZoomTo(PlayerView view, double? level)
    {
        view.zoomComboBox.SelectedItem = view.zoomComboBox.Items.Cast<PreviewZoomOption>()
            .Single(option => option.Level == level);
        HeadlessTestHelpers.Settle();
    }

    private static BitmapInterpolationMode Sampling(PlayerView view)
        => view.hdrView is { } hdr ? hdr.InterpolationMode : ((BitmapView)view.image).InterpolationMode;

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_frame_stays_smooth_until_a_zoom_shows_its_pixels_two_device_pixels_wide(bool hdr)
    {
        if (hdr)
            TestGpu.RequireAnyDevice();
        (_, PlayerView view, Window window) = await Open(hdr, 1920, 1080);
        try
        {
            Assert.That(view.ScreenZoom, Is.LessThan(1), "The levels below assume a fit under 100%.");
            BitmapInterpolationMode fit = Sampling(view);
            ZoomTo(view, 1);
            BitmapInterpolationMode atOne = Sampling(view);
            ZoomTo(view, 2);
            BitmapInterpolationMode atTwo = Sampling(view);
            ZoomTo(view, 8);
            BitmapInterpolationMode atEight = Sampling(view);
            ZoomTo(view, null);

            Assert.Multiple(() =>
            {
                Assert.That(fit, Is.EqualTo(BitmapInterpolationMode.HighQuality));
                Assert.That(atOne, Is.EqualTo(BitmapInterpolationMode.HighQuality));
                Assert.That(atTwo, Is.EqualTo(BitmapInterpolationMode.None));
                Assert.That(atEight, Is.EqualTo(BitmapInterpolationMode.None));
                Assert.That(Sampling(view), Is.EqualTo(BitmapInterpolationMode.HighQuality), "back at fit");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A frame smaller than the panel shows its pixels more than two device pixels wide at fit already. Fit and panning at
    // its scale stay smooth, and a level the user picks is sharp, even one under fit.
    [AvaloniaTest]
    public async Task A_small_frame_stays_smooth_at_fit_and_turns_sharp_at_a_level_the_user_picks()
    {
        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: false, 160, 90);
        try
        {
            Assert.That(view.ScreenZoom, Is.GreaterThan(2).And.LessThan(8), "The steps below assume this fit.");
            BitmapInterpolationMode fit = Sampling(view);
            editor.Player.FrameMatrix.Value *= BtlMatrix.CreateTranslation(30, 20);
            HeadlessTestHelpers.Settle();
            BitmapInterpolationMode panned = Sampling(view);
            // A level right next to fit, as 200% is where fit comes to 199.5%, is still a zoom the user chose.
            editor.Player.FrameMatrix.Value = BtlMatrix.CreateScale(1.0025f, 1.0025f);
            HeadlessTestHelpers.Settle();
            BitmapInterpolationMode nextToFit = Sampling(view);
            ZoomTo(view, 2);

            Assert.Multiple(() =>
            {
                Assert.That(fit, Is.EqualTo(BitmapInterpolationMode.HighQuality));
                Assert.That(panned, Is.EqualTo(BitmapInterpolationMode.HighQuality));
                Assert.That(nextToFit, Is.EqualTo(BitmapInterpolationMode.None));
                Assert.That(view.ScreenZoom, Is.EqualTo(2).Within(1e-3));
                Assert.That(Sampling(view), Is.EqualTo(BitmapInterpolationMode.None));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // At 800% a frame pixel is an 8 x 8 square of one color, so a row across the edge of a white rectangle holds the
    // background's color and white and nothing between them.
    [AvaloniaTest]
    public async Task At_800_percent_the_edge_of_a_shape_has_no_color_between_its_sides()
    {
        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: false, 1920, 1080);
        try
        {
            await AddRectangle(editor);
            ZoomTo(view, 8);
            Point edge = ZoomOnLeftEdge(editor, view, window);

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            (int Between, int Background, int White) row = ReadRow(frame, edge);

            Assert.Multiple(() =>
            {
                Assert.That(row.Background, Is.GreaterThan(20), "the row starts on the background");
                Assert.That(row.White, Is.GreaterThan(20), "the row ends on the rectangle");
                Assert.That(row.Between, Is.Zero);
            });
        }
        finally
        {
            window.Close();
        }
    }

    // An 800 x 500 white rectangle in the middle of the 1920 x 1080 frame, from x = 560 to 1360, drawn with nothing
    // selected so that no handles lie over its edges.
    internal static async Task AddRectangle(EditViewModel editor)
    {
        var rect = new RectShape();
        rect.Width.Value = 800;
        rect.Height.Value = 500;
        rect.Fill.Value = new SolidColorBrush(Colors.White);
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            MediaTime.Zero, MediaTime.FromSeconds(8), 0, new ElementSource.EngineObject(() => rect))], CancellationToken.None);
        HeadlessTestHelpers.Settle();
        editor.GetRequiredService<IEditorSelection>().Select(null);
        HeadlessTestHelpers.Settle();

        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered
            .Take(1)
            .Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }

    // Pans the zoomed frame so that the rectangle's left edge, at frame pixel 560, sits in the middle of the panel, and
    // returns where it is in the window.
    internal static Point ZoomOnLeftEdge(EditViewModel editor, PlayerView view, Window window)
    {
        double scale = view.image.Bounds.Width / 1920;
        Point before = view.image.TranslatePoint(new Point(560 * scale, 540 * scale), window)!.Value;
        Point middle = view.previewViewport.TranslatePoint(
            new Point(view.previewViewport.Bounds.Width / 2, view.previewViewport.Bounds.Height / 2), window)!.Value;
        editor.Player.FrameMatrix.Value *= BtlMatrix.CreateTranslation((float)(middle.X - before.X), 0);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        return view.image.TranslatePoint(new Point(560 * scale, 540 * scale), window)!.Value;
    }

    // Reads 48 pixels each side of the edge: those that match the row's first pixel, white, or neither.
    private static (int Between, int Background, int White) ReadRow(WriteableBitmap frame, Point edge)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        bool bgra = buffer.Format == PixelFormat.Bgra8888;
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);

        int y = (int)edge.Y;
        int first = (int)edge.X - 48;
        (int R, int G, int B) Pixel(int x)
        {
            int i = (y * buffer.RowBytes) + (x * 4);
            return (pixels[i + (bgra ? 2 : 0)], pixels[i + 1], pixels[i + (bgra ? 0 : 2)]);
        }

        (int R, int G, int B) background = Pixel(first);
        int between = 0, onBackground = 0, white = 0;
        for (int x = first; x < (int)edge.X + 48; x++)
        {
            (int r, int g, int b) = Pixel(x);
            if (Math.Max(Math.Abs(r - background.R), Math.Max(Math.Abs(g - background.G), Math.Abs(b - background.B))) <= 8)
                onBackground++;
            else if (Math.Min(r, Math.Min(g, b)) >= 247)
                white++;
            else
                between++;
        }

        return (between, onBackground, white);
    }
}

using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using SkiaSharp;
using BtlMatrix = Beutl.Graphics.Matrix;

namespace Beutl.HeadlessUITests;

// A headless window has no native control host, so the HDR preview's native view never draws here. These check where it
// is told to draw the frame, and the copy of the UI it draws over the frame.
[NonParallelizable]
[TestFixture]
public sealed class PlayerViewHdrPreviewTests
{
    private (bool Hdr, bool Guides, PreviewGuideKinds Kinds, string Color, float Opacity) _saved;

    private static EditorConfig Config => GlobalConfiguration.Instance.EditorConfig;

    [SetUp]
    public void SetUp()
    {
        _saved = (Config.UseHdrPreview.Value, Config.IsPreviewGuideEnabled.Value, Config.PreviewGuideKinds.Value,
            Config.PreviewGuideColor.Value, Config.PreviewGuideOpacity.Value);
    }

    [TearDown]
    public void TearDown()
    {
        Config.UseHdrPreview.Value = _saved.Hdr;
        Config.IsPreviewGuideEnabled.Value = _saved.Guides;
        Config.PreviewGuideKinds.Value = _saved.Kinds;
        Config.PreviewGuideColor.Value = _saved.Color;
        Config.PreviewGuideOpacity.Value = _saved.Opacity;
    }

    private static async Task<(EditViewModel Editor, PlayerView View, Window Window)> Open(bool hdr, double width, double height)
    {
        Config.UseHdrPreview.Value = hdr;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("hdr-preview", 1920, 1080);
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderPreview(editor);
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0), "the preview has a frame");
        return (editor, view, window);
    }

    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered
            .Take(1)
            .Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }

    // Where the slot holds the frame, in the native view's coordinates.
    private static Rect SlotIn(PlayerView view, Visual target)
    {
        return new Rect(view.image.Bounds.Size).TransformToAABB(view.image.TransformToVisual(target)!.Value);
    }

    // A native view follows neither the frame panel's transform nor its clip, so it takes the whole viewport, and the
    // frame's place comes from the slot.
    [AvaloniaTest]
    public async Task The_native_view_covers_the_viewport_and_draws_the_frame_where_the_slot_is()
    {
        TestGpu.RequireAnyDevice();
        (_, PlayerView view, Window window) = await Open(hdr: true, 900, 520);
        try
        {
            HdrBitmapView native = view.hdrView!;
            Assert.Multiple(() =>
            {
                Assert.That(view.image, Is.TypeOf<PreviewFrameSlot>());
                Assert.That(native.Parent, Is.SameAs(view.previewViewport));
                Assert.That(native.Bounds, Is.EqualTo(new Rect(view.previewViewport.Bounds.Size)));
                Assert.That(native.FrameRect, Is.EqualTo(SlotIn(view, native)));
                Assert.That(view.image.Bounds.Width / view.image.Bounds.Height, Is.EqualTo(16.0 / 9).Within(0.01));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Zooming_and_panning_move_the_frame_inside_the_native_view()
    {
        TestGpu.RequireAnyDevice();
        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: true, 900, 520);
        try
        {
            HdrBitmapView native = view.hdrView!;
            Rect viewBounds = native.Bounds;
            Rect fit = native.FrameRect!.Value;

            editor.Player.FrameMatrix.Value = BtlMatrix.CreateScale(4, 4) * BtlMatrix.CreateTranslation(-500, -300);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(native.Bounds, Is.EqualTo(viewBounds), "the native view stays where the viewport is");
                Assert.That(native.FrameRect!.Value.Width, Is.EqualTo(fit.Width * 4).Within(0.01));
                Assert.That(native.FrameRect!.Value.X, Is.EqualTo(fit.X * 4 - 500).Within(0.01));
                Assert.That(native.FrameRect, Is.EqualTo(SlotIn(view, native)));
                Assert.That(native.FrameRegion, Is.EqualTo(new Rect(viewBounds.Size)), "the zoomed frame fills the view");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A new frame changes the time in the footer, the seek bar and the player's own properties, none of which lies over
    // the frame, and the guides over it stay as they are, so playback records no copy and adds nothing to presenting its
    // frames.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task New_frames_leave_the_copy_alone(bool guides)
    {
        TestGpu.RequireAnyDevice();
        Config.PreviewGuideKinds.Value = PreviewGuideKinds.RuleOfThirds | PreviewGuideKinds.TitleSafe;
        Config.IsPreviewGuideEnabled.Value = guides;
        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: true, 900, 520);
        try
        {
            int invalidations = 0;
            view.hdrView!.Overlay!.Invalidated += (_, _) => invalidations++;
            Rational rate = editor.Player.GetFrameRate();

            for (int frame = 1; frame <= 5; frame++)
            {
                editor.Player.ApplyTimecodeSeek(MediaTime.FromFrames(frame, rate));
                await RenderPreview(editor);
            }

            Assert.That(invalidations, Is.Zero);
            if (guides)
            {
                // The copy does follow the guides.
                Config.PreviewGuideColor.Value = "#FFFF0000";
                HeadlessTestHelpers.Render();
                Assert.That(invalidations, Is.GreaterThan(0));
            }
        }
        finally
        {
            window.Close();
        }
    }

    // A player wider than the scene fits the frame to its height, the common layout in the editor, so the frame ends
    // where the seek bar's row begins and the seek bar, which reaches 3 px over the row above to be easy to grab, lies
    // over the frame's bottom. It draws nothing there, and seeking records no copy.
    [AvaloniaTest]
    public async Task Seeking_leaves_the_copy_alone_where_the_seek_bar_reaches_over_the_frame()
    {
        TestGpu.RequireAnyDevice();
        Config.IsPreviewGuideEnabled.Value = false;
        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: true, 1200, 450);
        try
        {
            HdrBitmapView native = view.hdrView!;
            PlayerSeekBar seekBar = view.GetVisualDescendants().OfType<PlayerSeekBar>().Single();
            Rect barInView = new Rect(seekBar.Bounds.Size).TransformToAABB(seekBar.TransformToVisual(native)!.Value);
            Assert.That(barInView.Intersects(native.FrameRegion), Is.True,
                $"the seek bar ({barInView}) reaches over the frame ({native.FrameRegion})");

            int invalidations = 0;
            native.Overlay!.Invalidated += (_, _) => invalidations++;
            Rational rate = editor.Player.GetFrameRate();
            for (int frame = 1; frame <= 5; frame++)
            {
                editor.Player.ApplyTimecodeSeek(MediaTime.FromFrames(frame * 7, rate));
                await RenderPreview(editor);
            }

            Assert.That(invalidations, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }

    // An empty scene renders a transparent frame, which shows black. The HDR preview draws the copy over black too, so
    // within the frame it shows what the SDR preview does: the guides, and the tool buttons a narrow player puts over
    // the frame.
    [AvaloniaTest]
    public async Task The_copy_over_a_black_frame_shows_what_the_SDR_preview_shows()
    {
        TestGpu.RequireAnyDevice();
        Config.PreviewGuideKinds.Value = PreviewGuideKinds.RuleOfThirds | PreviewGuideKinds.TitleSafe;
        Config.PreviewGuideColor.Value = "#FFFFFFFF";
        Config.PreviewGuideOpacity.Value = 1;
        Config.IsPreviewGuideEnabled.Value = true;

        (EditViewModel editor, PlayerView view, Window window) = await Open(hdr: false, 520, 520);
        try
        {
            Rect frame = SlotIn(view, window);
            using WriteableBitmap sdr = window.CaptureRenderedFrame()!;

            Config.UseHdrPreview.Value = true;
            HeadlessTestHelpers.Render();
            await RenderPreview(editor);
            HdrBitmapView native = view.hdrView!;
            Assert.That(SlotIn(view, window), Is.EqualTo(frame), "the frame stays where it was");

            using SKPicture picture = native.Overlay!.Record(native.Bounds.Size, native.FrameRegion, 1);
            Point origin = native.TranslatePoint(default, window)!.Value;
            using var copy = new SKBitmap(new SKImageInfo(
                (int)native.Bounds.Width, (int)native.Bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(copy))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPicture(picture);
            }

            (int Compared, int Lit, int WorstDifference) result = Compare(sdr, copy, frame, origin);
            Assert.Multiple(() =>
            {
                Assert.That(result.Lit, Is.GreaterThan(500), "the guides and the buttons are in the copy");
                Assert.That(result.WorstDifference, Is.LessThanOrEqualTo(2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A dialog draws in the window's overlay layer, which the native view covered. The frame panel shows black under the
    // native view here, and the copy over black shows the dialog and its smoke as the window does. The dialog has no text,
    // whose edges the window and the copy may smooth apart.
    [AvaloniaTest]
    public async Task A_dialog_over_the_player_is_in_the_copy_as_the_window_shows_it()
    {
        TestGpu.RequireAnyDevice();
        (_, PlayerView view, Window window) = await Open(hdr: true, 900, 520);
        var dialog = new FAContentDialog { Content = new Border { Background = Brushes.Fuchsia, Width = 200, Height = 80 } };
        Task? closed = null;
        try
        {
            closed = dialog.ShowAsync(window);
            await HdrOverlayTests.WaitUntilShown(dialog);

            HdrBitmapView native = view.hdrView!;
            Rect frame = SlotIn(view, window);
            using WriteableBitmap shown = window.CaptureRenderedFrame()!;
            using SKPicture picture = native.Overlay!.Record(native.Bounds.Size, native.FrameRegion, 1);
            Point origin = native.TranslatePoint(default, window)!.Value;
            using var copy = new SKBitmap(new SKImageInfo(
                (int)native.Bounds.Width, (int)native.Bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(copy))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPicture(picture);
            }

            (int Compared, int Lit, int WorstDifference) result = Compare(shown, copy, frame, origin);
            var corner = new PixelPoint((int)(frame.X - origin.X) + 2, (int)(frame.Y - origin.Y) + 2);
            Assert.Multiple(() =>
            {
                Assert.That(copy.GetPixel(corner.X, corner.Y).Alpha, Is.GreaterThan(0), "the smoke over the frame");
                Assert.That(result.Lit, Is.GreaterThan(500), "the dialog's content over the frame");
                Assert.That(result.WorstDifference, Is.LessThanOrEqualTo(2));
            });
        }
        finally
        {
            await HdrOverlayTests.CloseAsync(dialog, closed);
            window.Close();
        }
    }

    // Compares the SDR window's pixels with the copy over black (its premultiplied colors), inside the frame less its
    // edge pixels, which the frame's own edge shares.
    private static (int Compared, int Lit, int WorstDifference) Compare(
        WriteableBitmap sdr, SKBitmap copy, Rect frame, Point origin)
    {
        using ILockedFramebuffer buffer = sdr.Lock();
        bool bgra = buffer.Format == PixelFormat.Bgra8888;
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        ReadOnlySpan<byte> copied = copy.GetPixelSpan();

        int compared = 0, lit = 0, worst = 0;
        for (int y = (int)Math.Ceiling(frame.Y) + 1; y < (int)Math.Floor(frame.Bottom) - 1; y++)
        {
            for (int x = (int)Math.Ceiling(frame.X) + 1; x < (int)Math.Floor(frame.Right) - 1; x++)
            {
                int s = y * buffer.RowBytes + x * 4;
                int r = pixels[s + (bgra ? 2 : 0)], g = pixels[s + 1], b = pixels[s + (bgra ? 0 : 2)];

                int cx = x - (int)origin.X, cy = y - (int)origin.Y;
                int c = cy * copy.RowBytes + cx * 4;
                int cb = copied[c], cg = copied[c + 1], cr = copied[c + 2];

                worst = Math.Max(worst, Math.Max(Math.Abs(r - cr), Math.Max(Math.Abs(g - cg), Math.Abs(b - cb))));
                compared++;
                if (r + g + b > 150)
                    lit++;
            }
        }

        return (compared, lit, worst);
    }
}

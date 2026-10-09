using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Beutl.Controls;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using SkiaSharp;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class BitmapViewSourceLifetimeTests
{
    [AvaloniaTest]
    public void A_background_binding_update_cannot_remove_the_displayed_frame_after_its_ref_expires()
    {
        using var first = CreateFrame(SKColors.Red, 32, 16);
        using var expired = CreateFrame(SKColors.Green, 32, 16);
        var source = new FrameSource { Frame = first };
        var view = new BitmapView { Stretch = Stretch.Fill };
        using var binding = view.Bind(BitmapView.SourceProperty,
            new ReflectionBinding(nameof(FrameSource.Frame)) { Source = source });
        var window = new Window { Content = view, Width = 64, Height = 32, Background = Brushes.Black };
        try
        {
            window.Show();
            AssertCenterPixel(window, red: true);

            // Leave the UI dispatcher occupied until the posted binding value has expired.
            // Use a dedicated thread: awaiting a pool task synchronously can execute it inline
            // on the headless UI thread, which would remove the delayed-delivery condition.
            Exception? workerError = null;
            var worker = new Thread(() =>
            {
                try
                {
                    source.Frame = expired;
                    expired.Dispose();
                }
                catch (Exception ex)
                {
                    workerError = ex;
                }
            });
            worker.Start();
            Assert.That(worker.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(workerError, Is.Null);
            AssertCenterPixel(window, red: true);
        }
        finally
        {
            source.Frame = null;
            HeadlessTestHelpers.Settle();
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Expired_frame_keeps_the_last_displayed_pixels_until_a_live_frame_arrives()
    {
        using var first = CreateFrame(SKColors.Red, 32, 16);
        using var expired = CreateFrame(SKColors.Green, 64, 32);
        using var next = CreateFrame(SKColors.Blue, 32, 16);
        var view = new BitmapView { Source = first, Stretch = Stretch.Fill };
        var window = new Window { Content = view, Width = 64, Height = 32, Background = Brushes.Black };
        try
        {
            window.Show();
            AssertCenterPixel(window, red: true);

            // The player disposes superseded refs on the playback thread. A queued binding can
            // reach the UI after that disposal; the view still owns its clone of the last frame.
            first.Dispose();
            expired.Dispose();
            view.Source = expired;
            AssertCenterPixel(window, red: true);

            view.Source = next;
            next.Dispose();
            AssertCenterPixel(window, red: false);

            view.Source = null;
            HeadlessTestHelpers.Render();
            using var cleared = window.CaptureRenderedFrame()!;
            Assert.That(ReadCenterPixel(cleared), Is.EqualTo((0, 0, 0)));
        }
        finally
        {
            view.Source = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Sdr_expired_frame_does_not_collapse_the_displayed_image_size()
    {
        var view = new BitmapView();
        AssertRetainedSize(view, source => view.Source = source);
    }

    [AvaloniaTest]
    public void Hdr_expired_frame_does_not_collapse_the_displayed_image_size()
    {
        // Stay unattached: the frame handoff/measurement is shared with the native HDR host,
        // while headless cannot create its platform-specific child window.
        var view = new HdrBitmapView();
        AssertRetainedSize(view, source => view.Source = source);
    }

    private static void AssertRetainedSize(Control view, Action<Ref<BtlBitmap>?> setSource)
    {
        using var first = CreateFrame(SKColors.Red, 32, 16);
        using var expired = CreateFrame(SKColors.Green, 64, 32);
        using var next = CreateFrame(SKColors.Blue, 16, 8);
        try
        {
            setSource(first);
            view.Measure(Size.Infinity);
            Assert.That(view.DesiredSize, Is.EqualTo(new Size(32, 16)));

            first.Dispose();
            expired.Dispose();
            setSource(expired);
            view.Measure(Size.Infinity);
            Assert.Multiple(() =>
            {
                Assert.That(view.DesiredSize, Is.EqualTo(new Size(32, 16)));
                Assert.That(first.RefCount, Is.GreaterThan(0), "the view still owns the displayed bitmap");
            });

            setSource(next);
            view.Measure(Size.Infinity);
            Assert.That(view.DesiredSize, Is.EqualTo(new Size(16, 8)));

            setSource(null);
            view.Measure(Size.Infinity);
            Assert.Multiple(() =>
            {
                Assert.That(view.DesiredSize, Is.EqualTo(default(Size)));
                Assert.That(next.RefCount, Is.EqualTo(1), "explicit clearing releases the view's clone");
            });
        }
        finally
        {
            setSource(null);
        }
    }

    private static Ref<BtlBitmap> CreateFrame(SKColor color, int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return Ref<BtlBitmap>.Create(new BtlBitmap(bitmap));
    }

    private static void AssertCenterPixel(Window window, bool red)
    {
        HeadlessTestHelpers.Render();
        using var frame = window.CaptureRenderedFrame()!;
        Assert.That(frame, Is.Not.Null);
        Assert.That(ReadCenterPixel(frame), Is.EqualTo(red ? (255, 0, 0) : (0, 0, 255)));
    }

    private static unsafe (int R, int G, int B) ReadCenterPixel(Avalonia.Media.Imaging.WriteableBitmap frame)
    {
        using var framebuffer = frame.Lock();
        byte* pixel = (byte*)framebuffer.Address
                      + frame.PixelSize.Height / 2 * framebuffer.RowBytes
                      + frame.PixelSize.Width / 2 * 4;
        if (framebuffer.Format == PixelFormat.Rgba8888)
            return (pixel[0], pixel[1], pixel[2]);

        Assert.That(framebuffer.Format, Is.EqualTo(PixelFormat.Bgra8888));
        return (pixel[2], pixel[1], pixel[0]);
    }

    private sealed class FrameSource : INotifyPropertyChanged
    {
        public Ref<BtlBitmap>? Frame
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Frame)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

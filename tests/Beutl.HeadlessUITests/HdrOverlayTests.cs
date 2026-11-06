using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Testing.Headless;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

// The copy of what lies over an HDR preview's frame, in a viewport laid out like the player's: the native view fills a
// grid beside a panel that holds the frame's slot, with a layer before the slot (under the frame), one after it (over
// the frame), and a sibling of the panel over both.
[TestFixture]
public sealed class HdrOverlayTests
{
    private static readonly Rect s_frame = new(50, 50, 100, 100);

    private sealed record Viewport(
        Window Window, HdrBitmapView View, Panel FramePanel, Border Under, Control Slot, Border Over, Border Top,
        Border Outside, HdrOverlay Overlay);

    private static Border Square(IBrush brush, double x, double y, double size)
    {
        return new Border
        {
            Background = brush,
            Width = size,
            Height = size,
            Margin = new Thickness(x, y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
    }

    private static Viewport Open()
    {
        var view = new HdrBitmapView();
        Border under = Square(Brushes.Red, 0, 0, 200);
        var slot = new Border
        {
            Width = s_frame.Width,
            Height = s_frame.Height,
            Margin = new Thickness(s_frame.X, s_frame.Y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Border over = Square(Brushes.Blue, 60, 60, 20);
        var framePanel = new Panel { Children = { under, slot, over } };
        Border top = Square(Brushes.Lime, 130, 130, 10);
        Border outside = Square(Brushes.Yellow, 0, 0, 20);
        var grid = new Grid { Children = { view, framePanel, top, outside } };
        var window = new Window { Content = grid, Width = 200, Height = 200 };
        window.Show();
        view.FrameRect = s_frame;
        HeadlessTestHelpers.Render();

        var overlay = new HdrOverlay(slot, grid, view);
        return new Viewport(window, view, framePanel, under, slot, over, top, outside, overlay);
    }

    private static SKBitmap Draw(Viewport viewport)
    {
        using SKPicture picture = viewport.Overlay.Record(new Size(200, 200), viewport.View.FrameRegion, 1);
        var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(picture);
        return bitmap;
    }

    private static int CountInvalidations(Viewport viewport, Action change)
    {
        int count = 0;
        viewport.Overlay.Invalidated += (_, _) => count++;
        change();
        HeadlessTestHelpers.Render();
        return count;
    }

    [AvaloniaTest]
    public void The_copy_holds_what_lies_over_the_frame_and_nothing_under_or_around_it()
    {
        Viewport viewport = Open();
        try
        {
            using SKBitmap copy = Draw(viewport);

            Assert.Multiple(() =>
            {
                Assert.That(copy.GetPixel(70, 70), Is.EqualTo(SKColors.Blue), "the layer over the frame");
                Assert.That(copy.GetPixel(135, 135), Is.EqualTo(new SKColor(0, 255, 0)), "the panel's sibling over it");
                Assert.That(copy.GetPixel(100, 100).Alpha, Is.Zero, "the layer under the frame");
                Assert.That(copy.GetPixel(10, 10).Alpha, Is.Zero, "around the frame the UI shows itself");
            });
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // Zooming transforms the panel, and the copy takes the frame's place with it.
    [AvaloniaTest]
    public void The_copy_follows_the_transform_of_the_panel_that_holds_the_frame()
    {
        Viewport viewport = Open();
        try
        {
            viewport.FramePanel.RenderTransformOrigin = RelativePoint.TopLeft;
            viewport.FramePanel.RenderTransform = new ScaleTransform(2, 2);
            viewport.View.FrameRect = new Rect(100, 100, 200, 200);
            HeadlessTestHelpers.Render();

            using SKBitmap copy = Draw(viewport);

            Assert.Multiple(() =>
            {
                Assert.That(copy.GetPixel(150, 150), Is.EqualTo(SKColors.Blue), "the layer over the frame, zoomed");
                Assert.That(copy.GetPixel(110, 110).Alpha, Is.Zero);
                Assert.That(copy.GetPixel(70, 70).Alpha, Is.Zero, "the frame moved off this point");
            });
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    [AvaloniaTest]
    public void A_change_over_the_frame_invalidates_the_copy()
    {
        Viewport viewport = Open();
        try
        {
            Assert.That(CountInvalidations(viewport, () => viewport.Over.Background = Brushes.Green), Is.GreaterThan(0));
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // The player's footer changes its time every frame of playback, and the copy must not be drawn again for it.
    [AvaloniaTest]
    public void A_change_around_the_frame_leaves_the_copy()
    {
        Viewport viewport = Open();
        try
        {
            Assert.That(CountInvalidations(viewport, () => viewport.Outside.Background = Brushes.Green), Is.Zero);
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    [AvaloniaTest]
    public void An_ancestor_of_the_slot_counts_only_for_where_it_puts_the_layers_over_the_frame()
    {
        Viewport viewport = Open();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(CountInvalidations(viewport, () => viewport.FramePanel.Background = Brushes.Green), Is.Zero);
                Assert.That(
                    CountInvalidations(viewport, () => viewport.FramePanel.RenderTransform = new ScaleTransform(2, 2)),
                    Is.GreaterThan(0));
            });
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // A control that draws something new without a property changing says so through the overlay's helper.
    [AvaloniaTest]
    public void Drawing_that_changes_without_a_property_invalidates_through_the_helper()
    {
        Viewport viewport = Open();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(CountInvalidations(viewport, () => HdrOverlay.InvalidateVisual(viewport.Over)), Is.GreaterThan(0));
                Assert.That(CountInvalidations(viewport, () => HdrOverlay.InvalidateVisual(viewport.Outside)), Is.Zero);
            });
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // A crop selection is added to the panel while it is dragged out.
    [AvaloniaTest]
    public void A_visual_added_over_the_frame_invalidates_the_copy_once_it_has_its_place()
    {
        Viewport viewport = Open();
        try
        {
            Border added = Square(Brushes.Magenta, 100, 100, 10);
            Assert.That(CountInvalidations(viewport, () => viewport.FramePanel.Children.Add(added)), Is.GreaterThan(0));

            using SKBitmap copy = Draw(viewport);
            Assert.That(copy.GetPixel(105, 105), Is.EqualTo(SKColors.Magenta));

            Assert.That(CountInvalidations(viewport, () => added.Background = Brushes.Green), Is.GreaterThan(0));
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    [AvaloniaTest]
    public void A_disposed_copy_follows_nothing()
    {
        Viewport viewport = Open();
        try
        {
            int count = 0;
            viewport.Overlay.Invalidated += (_, _) => count++;
            viewport.Overlay.Dispose();

            viewport.Over.Background = Brushes.Green;
            HdrOverlay.InvalidateVisual(viewport.Over);
            HeadlessTestHelpers.Render();

            Assert.That(count, Is.Zero);
        }
        finally
        {
            viewport.Window.Close();
        }
    }
}

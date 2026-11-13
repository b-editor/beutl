using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using SkiaSharp;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

// The copy of what lies over an HDR preview's frame, in a viewport laid out like the player's: the native view fills a
// grid beside a panel that holds the frame's slot, with a layer before the slot (under the frame), one after it (over
// the frame), and a sibling of the panel over both. The grid is the copy's root, and the window holds it.
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

    // With a panel beside, the viewport takes the window's left 200 units, and the panel, drawn after it as the dock draws
    // the panels beside the player, the 100 units right of it.
    private static Viewport Open(Control? beside = null)
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
        Control content = grid;
        if (beside != null)
        {
            Grid.SetColumn(beside, 1);
            content = new Grid { ColumnDefinitions = new ColumnDefinitions("200,100"), Children = { grid, beside } };
        }

        var window = new Window { Content = content, Width = beside == null ? 200 : 300, Height = 200 };
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

    // The immediate renderer culls a layer's children against a rectangle at the layer's top left corner, not where the
    // frame shows the layer, which a zoomed frame moves further in.
    [AvaloniaTest]
    public void The_copy_holds_the_children_of_a_layer_where_a_zoomed_frame_shows_them()
    {
        Viewport viewport = Open();
        try
        {
            var child = new Border { Background = Brushes.Fuchsia, Width = 10, Height = 10 };
            Canvas.SetLeft(child, 85);
            Canvas.SetTop(child, 85);
            viewport.FramePanel.Children.Add(new Canvas { Children = { child } });
            viewport.FramePanel.RenderTransformOrigin = RelativePoint.TopLeft;
            viewport.FramePanel.RenderTransform = new ScaleTransform(2, 2);
            viewport.View.FrameRect = new Rect(100, 100, 200, 200);
            HeadlessTestHelpers.Render();

            using SKBitmap copy = Draw(viewport);

            Assert.That(copy.GetPixel(180, 180), Is.EqualTo(SKColors.Fuchsia));
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // The path editor draws the control lines of the curve it edits from a control without a size of its own, which
    // the immediate renderer culls.
    [AvaloniaTest]
    public async Task The_copy_holds_the_control_lines_of_the_curve_the_path_editor_edits()
    {
        var figure = new Beutl.Media.PathFigure();
        figure.Segments.Add(new Beutl.Media.LineSegment(50, 50));
        Beutl.Media.CubicBezierSegment curve =
            PathEditingOperations.Cubic(new BtlPoint(100, 0), new BtlPoint(150, 150), new BtlPoint(250, 150));
        figure.Segments.Add(curve);
        using PathEditorInteractionTests.Fixture fixture =
            await PathEditorInteractionTests.Fixture.Create(pathFigure: figure);
        PathEditorViewModel model = fixture.Editor.Player.PathEditor;
        var view = new HdrBitmapView();
        var slot = new Border();
        var editor = new PathEditorView { DataContext = model };
        var grid = new Grid { Children = { view, new Panel { Children = { slot, editor } } } };
        fixture.Window.Content = grid;
        model.FigureContext.Value = fixture.Model.FigureContext.Value;
        model.SelectedOperation.Value = curve;
        // The geometry the editor draws from comes through the render thread.
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            Beutl.Graphics.Rendering.RenderThread.Dispatcher.Invoke(
                static () => { }, Beutl.Threading.DispatchPriority.Low, timeout.Token);
        }

        HeadlessTestHelpers.Render(3);
        // Without a frame rectangle the view takes the frame's size, and it has no frame here.
        view.FrameRect = new Rect(grid.Bounds.Size);
        HeadlessTestHelpers.Render();
        var overlay = new HdrOverlay(slot, grid, view);
        try
        {
            // Halfway along the line from the curve's end to its second control point, between their thumbs.
            Control controlThumb = editor.FindThumb(curve, curve.ControlPoint2)!;
            Assert.That(controlThumb.IsVisible, Is.True, "the selected curve shows its control points");
            Point end = Center(editor.FindThumb(curve, curve.EndPoint)!);
            Point control = Center(controlThumb);
            var middle = new Point((end.X + control.X) / 2, (end.Y + control.Y) / 2);
            Assert.That(view.FrameRegion.Contains(middle), Is.True, $"the line crosses the frame at {middle}");
            Assert.That(model.GeometryResource.Value, Is.Not.Null, "the editor has the geometry it draws the lines from");
            using (WriteableBitmap shown = fixture.Window.CaptureRenderedFrame()!)
            {
                Point at = view.TranslatePoint(middle, fixture.Window)!.Value;
                Assert.That(Differs(shown, at, new Point(at.X, at.Y - 12)), Is.True, "the window shows the line");
            }

            using SKPicture picture = overlay.Record(view.Bounds.Size, view.FrameRegion, 1);
            using var copy = new SKBitmap(new SKImageInfo(
                (int)view.Bounds.Width, (int)view.Bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(copy))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPicture(picture);
            }

            // The line is a pixel thick, and it may fall between two rows.
            byte alpha = Math.Max(copy.GetPixel((int)middle.X, (int)middle.Y).Alpha,
                Math.Max(copy.GetPixel((int)middle.X, (int)middle.Y - 1).Alpha,
                    copy.GetPixel((int)middle.X, (int)middle.Y + 1).Alpha));
            Assert.That(alpha, Is.GreaterThan(0));
        }
        finally
        {
            overlay.Dispose();
            editor.DataContext = null;
            model.SelectedOperation.Value = null;
            model.FigureContext.Value = null;
        }

        Point Center(Control thumb)
            => thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), view)!.Value;
    }

    // Whether a captured pixel on the rows around point differs from the one at reference.
    private static bool Differs(WriteableBitmap bitmap, Point point, Point reference)
    {
        using ILockedFramebuffer buffer = bitmap.Lock();
        uint background = Read((int)reference.X, (int)reference.Y);
        for (int dy = -1; dy <= 1; dy++)
        {
            uint pixel = Read((int)point.X, (int)point.Y + dy);
            for (int shift = 0; shift < 24; shift += 8)
            {
                if (Math.Abs((int)((pixel >> shift) & 0xFF) - (int)((background >> shift) & 0xFF)) > 16)
                    return true;
            }
        }

        return false;

        uint Read(int x, int y) => (uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
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

    // A window draws its dialogs in its overlay layer, after all of its content, and the native view used to cover the
    // part over the frame. The dialog has no text, whose edges the window and the copy may smooth apart.
    [AvaloniaTest]
    public async Task The_copy_holds_a_dialog_the_window_shows_over_the_frame()
    {
        Viewport viewport = Open();
        var content = new Border { Background = Brushes.Fuchsia, Width = 40, Height = 40 };
        var dialog = new FAContentDialog { Content = content };
        Task<FAContentDialogResult>? closed = null;
        try
        {
            int shown = CountInvalidations(viewport, () => closed = dialog.ShowAsync(viewport.Window));
            await WaitUntilShown(dialog);

            using (SKBitmap copy = Draw(viewport))
            using (WriteableBitmap window = viewport.Window.CaptureRenderedFrame()!)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(shown, Is.GreaterThan(0), "showing the dialog invalidates the copy");
                    Assert.That(copy.GetPixel(100, 100), Is.EqualTo(SKColors.Fuchsia), "the dialog's content");
                    Assert.That(copy.GetPixel(51, 51).Alpha, Is.GreaterThan(0), "the dialog or the smoke around it");
                    Assert.That(WorstDifferenceOverUnder(window, copy), Is.LessThanOrEqualTo(2),
                        "over the red the frame covers, the copy shows what the window does");
                });
            }

            Assert.That(CountInvalidations(viewport, () => HdrOverlay.InvalidateVisual(content)), Is.GreaterThan(0),
                "a control in the dialog invalidates the copy through the helper");

            int hidden = CountInvalidations(viewport, dialog.Hide);
            await closed!.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render();
            using SKBitmap after = Draw(viewport);
            Assert.Multiple(() =>
            {
                Assert.That(hidden, Is.GreaterThan(0), "closing the dialog invalidates the copy");
                Assert.That(after.GetPixel(100, 100).Alpha, Is.Zero, "nothing of the dialog stays");
            });
        }
        finally
        {
            await CloseAsync(dialog, closed);
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // The dock draws the panels beside the player after it, and the timeline among them changes all through playback.
    // One beside the view draws nothing over the frame, so the copy follows only where it lies until it comes over it.
    [AvaloniaTest]
    public void A_visual_drawn_after_the_root_counts_once_it_comes_over_the_frame()
    {
        var inside = new Border { Background = Brushes.Orange };
        var beside = new Border { Child = inside };
        Viewport viewport = Open(beside);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(viewport.Overlay.Follows(inside), Is.False, "what a panel beside the view draws");
                Assert.That(CountInvalidations(viewport, () => inside.Background = Brushes.Green), Is.Zero,
                    "a change beside the view");
            });

            // Moved by layout, as a dragged splitter moves a panel, over the frame's middle.
            int moved = CountInvalidations(viewport, () => beside.Margin = new Thickness(-150, 0, 150, 0));
            using SKBitmap copy = Draw(viewport);
            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.GreaterThan(0), "coming over the frame");
                Assert.That(copy.GetPixel(100, 100), Is.EqualTo(SKColors.Green), "drawn over the frame");
                Assert.That(viewport.Overlay.Follows(inside), Is.True, "what it draws over the frame");
                Assert.That(CountInvalidations(viewport, () => inside.Background = Brushes.Orange), Is.GreaterThan(0),
                    "a change over the frame");
            });
        }
        finally
        {
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // A player leaves its window when its dock tab closes. The window's layers must not keep its copy, and so the player,
    // following them; one that comes back follows them again.
    [AvaloniaTest]
    public async Task A_root_out_of_the_window_follows_none_of_its_dialogs()
    {
        Viewport viewport = Open();
        var dialog = new FAContentDialog { Content = new Border { Background = Brushes.Fuchsia, Width = 40, Height = 40 } };
        Task? shown = null;
        try
        {
            OverlayLayer layer = OverlayLayer.GetOverlayLayer(viewport.Window)!;
            HeadlessTestHelpers.Render();
            Assert.That(viewport.Overlay.Follows(layer), Is.True, "the window's overlay layer, with the root in the window");

            object? content = viewport.Window.Content;
            viewport.Window.Content = null;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(viewport.Overlay.Follows(layer), Is.False, "the layer, once the root is out of the window");
                Assert.That(CountInvalidations(viewport, () => shown = dialog.ShowAsync(viewport.Window)), Is.Zero,
                    "a dialog shown while the root is out of the window");
            });
            await WaitUntilShown(dialog);

            viewport.Window.Content = content;
            HeadlessTestHelpers.Render();
            using SKBitmap copy = Draw(viewport);
            Assert.Multiple(() =>
            {
                Assert.That(copy.GetPixel(100, 100), Is.EqualTo(SKColors.Fuchsia), "the dialog, once the root is back");
                Assert.That(viewport.Overlay.Follows(dialog), Is.True, "the dialog is followed again");
                Assert.That(CountInvalidations(viewport, () => HdrOverlay.InvalidateVisual(dialog)), Is.GreaterThan(0),
                    "and invalidates the copy");
            });
        }
        finally
        {
            await CloseAsync(dialog, shown);
            viewport.Overlay.Dispose();
            viewport.Window.Close();
        }
    }

    // FluentAvalonia takes a dialog out of its window 200 ms after hiding it, and throws then for one that never showed.
    // Waiting for the dialog a test showed keeps that out of the next test.
    internal static async Task CloseAsync(FAContentDialog dialog, Task? shown)
    {
        if (shown is not { IsCompleted: false })
            return;

        dialog.Hide();
        await shown.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // A dialog fades in over real time, so frames are drawn until it is fully shown.
    internal static async Task WaitUntilShown(FAContentDialog dialog)
    {
        Panel? root = null;
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            root ??= dialog.GetVisualDescendants().OfType<Panel>().FirstOrDefault(panel => panel.Name == "LayoutRoot");
            return root is { Opacity: >= 1 };
        }, message: "The dialog did not finish fading in.");

        // The dialog's scale settles on the same frame as its opacity, or the one after.
        HeadlessTestHelpers.Render(2);
    }

    // The largest difference of a color component between what the window shows inside the frame, less its edge pixels,
    // and the copy drawn over the red that lies under the frame there.
    private static int WorstDifferenceOverUnder(WriteableBitmap window, SKBitmap copy)
    {
        using ILockedFramebuffer buffer = window.Lock();
        bool bgra = buffer.Format == PixelFormat.Bgra8888;
        ReadOnlySpan<byte> copied = copy.GetPixelSpan();
        int worst = 0;
        for (int y = (int)s_frame.Y + 1; y < (int)s_frame.Bottom - 1; y++)
        {
            for (int x = (int)s_frame.X + 1; x < (int)s_frame.Right - 1; x++)
            {
                int pixel = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                int r = (pixel >> (bgra ? 16 : 0)) & 0xFF, g = (pixel >> 8) & 0xFF, b = (pixel >> (bgra ? 0 : 16)) & 0xFF;

                // The copy is premultiplied, so red under it adds red times what it leaves uncovered.
                int c = y * copy.RowBytes + x * 4;
                int cb = copied[c], cg = copied[c + 1], cr = copied[c + 2], ca = copied[c + 3];
                cr += 255 - ca;

                worst = Math.Max(worst, Math.Max(Math.Abs(r - cr), Math.Max(Math.Abs(g - cg), Math.Abs(b - cb))));
            }
        }

        return worst;
    }
}

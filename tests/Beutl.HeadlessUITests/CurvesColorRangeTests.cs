using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.CurvesTab.Services;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.Views;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using AvaPoint = Avalonia.Point;
using AvaVector = Avalonia.Vector;
using BtlPoint = Beutl.Graphics.Point;
using Color = Beutl.Media.Color;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

// The curves tab's eyedropper and color range (#3310). A click on the preview picks a color for the hue curves from the
// frame drawn without the effect, so a curve that already changed the color does not move what is picked, and the
// range around the color shapes the curve shown there and nowhere else.
[NonParallelizable]
[TestFixture]
public sealed class CurvesColorRangeTests
{
    private const int FrameWidth = 64;
    private const int FrameHeight = 32;

    // Orange on the left half of the frame and blue on the right.
    private static readonly Color s_left = Color.FromArgb(255, 230, 110, 40);
    private static readonly Color s_right = Color.FromArgb(255, 40, 90, 230);

    [AvaloniaTest]
    public async Task A_picked_color_is_read_before_the_curves_and_its_range_changes_that_color_only()
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("curves-color-range", FrameWidth, FrameHeight);
        var curves = new Curves();
        // Hue vs hue turns every color a quarter of the way round, so the preview no longer shows what the curves read.
        curves.HueVsHue.Value = Level(0.75f);
        await TestShell.AddElementAsync(editor, () => CreateShape(curves), MediaTime.Zero, MediaTime.FromSeconds(2));
        int undoCount = editor.HistoryManager.UndoCount;

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        model.SelectedGroup.Value = CurveGroup.HueVsHue;
        var curvesView = new CurvesTabView { DataContext = model };
        var playerView = new PlayerView { DataContext = editor.Player };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(curvesView, 1);
        layout.Children.Add(playerView);
        layout.Children.Add(curvesView);
        var window = new Window { Content = layout, Width = 1000, Height = 500 };
        Beutl.Controls.ControlConfiguration.SetConfiguration(window, TestShell.Configuration);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            await RenderThread.Dispatcher.InvokeAsync(static () => { });
            HeadlessTestHelpers.Render(2);
            var pickButton = curvesView.FindControl<ToggleButton>("PickColorButton")!;
            Assert.That(pickButton.IsEffectivelyVisible, Is.True, "Hue vs hue offers the eyedropper.");

            // A right click on the frame and Escape both end a pick without a color.
            HeadlessTestHelpers.Click(pickButton);
            Assert.That(editor.Player.PointPick.Value, Is.Not.Null, "The preview waits for a click.");
            AvaPoint left = OnFrame(playerView, window, 16, 16);
            window.MouseDown(left, MouseButton.Right);
            window.MouseUp(left, MouseButton.Right);
            HeadlessTestHelpers.Settle();
            Assert.That((editor.Player.PointPick.Value, model.IsPickingColor.Value, model.HasPickedColor.Value),
                Is.EqualTo(((PreviewPointPick?)null, false, false)));

            HeadlessTestHelpers.Click(pickButton);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            HeadlessTestHelpers.Settle();
            Assert.That((editor.Player.PointPick.Value, model.IsPickingColor.Value), Is.EqualTo(((PreviewPointPick?)null, false)));

            // Over the frame the curve shows where the color under the pointer stands, and a click picks it.
            HeadlessTestHelpers.Click(pickButton);
            window.MouseMove(left);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.HoverPosition.Value is not null, message: "No hover marker.");
            window.MouseDown(left, MouseButton.Left);
            window.MouseUp(left, MouseButton.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.HasPickedColor.Value, message: "No color was picked.");

            float hue = HueOf(s_left);
            Assert.Multiple(() =>
            {
                Assert.That(model.IsPickingColor.Value, Is.False);
                Assert.That(model.MarkerPosition.Value, Is.EqualTo(hue).Within(1.0 / 360), "Hue vs hue reads the color itself.");
                Assert.That(model.ShowColorRange.Value, Is.True);
                Assert.That(model.RangeValue.Value, Is.EqualTo(90).Within(0.1), "The curve already turns it by 90 degrees.");
            });

            // Hue vs saturation reads the hue after hue vs hue has turned it.
            model.SelectedGroup.Value = CurveGroup.HueVsSaturation;
            HeadlessTestHelpers.Settle();
            float turned = (hue + 0.25f) % 1;
            Assert.Multiple(() =>
            {
                Assert.That(model.MarkerPosition.Value, Is.EqualTo(turned).Within(1.0 / 360));
                Assert.That(model.RangeValue.Value, Is.EqualTo(100).Within(0.1));
                Assert.That(model.RangeValueUnit.Value, Is.EqualTo("%"));
            });

            // No saturation at the picked color takes the orange's color away and leaves the blue's.
            model.ConfirmRangeValue(0);
            HeadlessTestHelpers.Settle();
            CurveMap shaped = curves.HueVsSaturation.Value;
            using (Bitmap frame = await editor.Player.DrawFrameAtFullScale())
            using (Bitmap pixels = frame.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb))
            {
                Bgra8888 orange = pixels.GetRow<Bgra8888>(16)[16];
                Bgra8888 blue = pixels.GetRow<Bgra8888>(16)[48];
                Assert.Multiple(() =>
                {
                    Assert.That(Chroma(orange), Is.LessThanOrEqualTo(2), $"The picked color is gray: {orange.R},{orange.G},{orange.B}");
                    Assert.That(Chroma(blue), Is.GreaterThan(100), $"Another color keeps its own: {blue.R},{blue.G},{blue.B}");
                    Assert.That(shaped.Evaluate(turned), Is.EqualTo(0).Within(1e-3f));
                    Assert.That(shaped.Evaluate((turned + 0.5f) % 1), Is.EqualTo(0.5f).Within(1e-6f));
                    Assert.That(model.MarkerPosition.Value, Is.EqualTo(turned).Within(1.0 / 360), "The edit does not move the color.");
                    Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                });
            }

            // Picking the same pixel again reads the same color, though the preview now shows it gray.
            HeadlessTestHelpers.Click(pickButton);
            window.MouseMove(left);
            window.MouseDown(left, MouseButton.Left);
            window.MouseUp(left, MouseButton.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsPickingColor.Value, message: "The pick did not end.");
            Assert.That(model.MarkerPosition.Value, Is.EqualTo(turned).Within(1.0 / 360));

            Assert.That(await editor.UndoAsync(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(Level(0.5f)));
                Assert.That(model.RangeValue.Value, Is.EqualTo(100).Within(0.1), "The box follows the undo.");
            });
        }
        finally
        {
            curvesView.DataContext = null;
            window.Close();
        }
    }

    // A box being typed in keeps its text while the curve changes some other way, as by a drag of its points: the value
    // shown would reach it as typing and shape the curve again, and the box would confirm it when it lost the focus.
    [AvaloniaTest]
    public async Task A_box_with_the_focus_does_not_shape_a_curve_changed_some_other_way()
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("curves-color-range-focus", FrameWidth, FrameHeight);
        var curves = new Curves();
        await TestShell.AddElementAsync(editor, () => CreateShape(curves), MediaTime.Zero, MediaTime.FromSeconds(2));

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        model.SelectedGroup.Value = CurveGroup.HueVsSaturation;
        (Window window, CurvesTabView curvesView) = await ShowAndPickAsync(editor, model);
        try
        {
            var box = curvesView.FindControl<NumberEditor<float>>("RangeValueEditor")!;
            TextBox text = box.GetVisualDescendants().OfType<TextBox>().First();
            text.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(text.IsKeyboardFocusWithin, Is.True);
            int undoCount = editor.HistoryManager.UndoCount;

            CurveMap changed = Level(0.3f);
            curves.HueVsSaturation.Value = changed;
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(changed), "The curve was shaped again.");
                Assert.That(text.Text, Is.EqualTo("100"), "The box keeps what it showed while it has the focus.");
            });

            curvesView.FindControl<ToggleButton>("PickColorButton")!.Focus();
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(changed), "Losing the focus confirmed a value.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
                Assert.That(text.Text, Is.EqualTo("60"), "The box shows the curve once it loses the focus.");
            });
        }
        finally
        {
            curvesView.DataContext = null;
            window.Close();
        }
    }

    // A scrub that comes back to where it started is not confirmed by its box, yet its steps shaped the curve and took
    // the hand-made point inside the range; the release confirms it, so it is an undo step and Undo gives the point back.
    [AvaloniaTest]
    public async Task A_scrub_that_comes_back_to_its_start_is_an_undo_step()
    {
        TestGpu.RequireAnyDevice();
        using PointerLockScope pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("curves-color-range-scrub", FrameWidth, FrameHeight);
        var curves = new Curves();
        await TestShell.AddElementAsync(editor, () => CreateShape(curves), MediaTime.Zero, MediaTime.FromSeconds(2));
        var handMade = new CurveMap(
            [new CurveControlPoint(0, 0.5f), new CurveControlPoint(0.08f, 0.7f), new CurveControlPoint(1, 0.5f)]);
        editor.HistoryManager.ExecuteInTransaction(() => curves.HueVsSaturation.Value = handMade, "Hand edit");

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        model.SelectedGroup.Value = CurveGroup.HueVsSaturation;
        (Window window, CurvesTabView curvesView) = await ShowAndPickAsync(editor, model);
        try
        {
            int undoCount = editor.HistoryManager.UndoCount;
            var box = curvesView.FindControl<NumberEditor<float>>("RangeValueEditor")!;
            Avalonia.Controls.TextBlock header = box.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(x => x.Name == "PART_HeaderTextBlock");
            AvaPoint start = header.TranslatePoint(new AvaPoint(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            float before = box.Value;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new AvaVector(10, 0), RawInputModifiers.LeftMouseButton);
            Assert.That(curves.HueVsSaturation.Value, Is.Not.EqualTo(handMade), "The scrub shaped the curve.");
            window.MouseMove(start, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(box.Value, Is.EqualTo(before));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            });
            Assert.That(await editor.UndoAsync(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(handMade));
        }
        finally
        {
            curvesView.DataContext = null;
            window.Close();
        }
    }

    // A handle that reaches past the next point makes x go back and forth along its piece, which the range cannot cut
    // without changing the curve outside it. A value or a width whose range would cut it leaves the curve as it is, and
    // its box shows again what the curve has: the value at the color, or the width the curve was last shaped with.
    [AvaloniaTest]
    public async Task A_range_that_would_cut_a_piece_going_back_and_forth_leaves_the_curve_as_it_is()
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("curves-color-range-crossing", FrameWidth, FrameHeight);
        var curves = new Curves();
        await TestShell.AddElementAsync(editor, () => CreateShape(curves), MediaTime.Zero, MediaTime.FromSeconds(2));
        // The orange is at hue 22 degrees, and the piece from 72 to 144 degrees goes back and forth.
        var crossing = new CurveMap(
        [
            new CurveControlPoint(0, 0.5f),
            new CurveControlPoint(new BtlPoint(0.2f, 0.5f), default, new BtlPoint(0.4f, 0.2f)),
            new CurveControlPoint(new BtlPoint(0.4f, 0.7f), new BtlPoint(-0.4f, 0), default),
            new CurveControlPoint(1, 0.5f),
        ]);
        editor.HistoryManager.ExecuteInTransaction(() => curves.HueVsSaturation.Value = crossing, "Hand edit");

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        model.SelectedGroup.Value = CurveGroup.HueVsSaturation;
        (Window window, CurvesTabView curvesView) = await ShowAndPickAsync(editor, model);
        try
        {
            int undoCount = editor.HistoryManager.UndoCount;
            var pickButton = curvesView.FindControl<ToggleButton>("PickColorButton")!;

            // 200 degrees reach into the piece.
            model.ConfirmRangeWidth(200);
            TextBox value = TypeInto(window, curvesView, "RangeValueEditor", "150", pickButton);
            Assert.Multiple(() =>
            {
                Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(crossing));
                Assert.That(value.Text, Is.EqualTo("100"), "The box shows the curve's value again.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            });

            // 60 degrees end before it, and a width that reaches it again keeps the curve as those shaped it.
            model.ConfirmRangeWidth(60);
            model.ConfirmRangeValue(150);
            HeadlessTestHelpers.Settle();
            CurveMap shaped = curves.HueVsSaturation.Value;
            Assert.That(shaped, Is.Not.EqualTo(crossing), "60 degrees shape the curve.");

            TextBox width = TypeInto(window, curvesView, "RangeWidthEditor", "200", pickButton);
            Assert.Multiple(() =>
            {
                Assert.That(curves.HueVsSaturation.Value, Is.EqualTo(shaped));
                Assert.That(model.RangeWidth.Value, Is.EqualTo(60));
                Assert.That(width.Text, Is.EqualTo("60"), "The box shows the width the curve has again.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            });
        }
        finally
        {
            curvesView.DataContext = null;
            window.Close();
        }
    }

    // Another effect that takes the tab keeps a hue curve shown and the eyedropper offered, so the pick of the first
    // ends with it rather than leaving the button on and the preview waiting for nothing.
    [AvaloniaTest]
    public async Task Moving_the_tab_to_another_effect_ends_a_pick()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("curves-color-range-retarget", FrameWidth, FrameHeight);
        var first = new Curves();
        var second = new Curves();
        await TestShell.AddElementAsync(editor, () => CreateShape(first), MediaTime.Zero, MediaTime.FromSeconds(2));
        await TestShell.AddElementAsync(editor, () => CreateShape(second), MediaTime.Zero, MediaTime.FromSeconds(2), layer: 1);

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = first;
        model.SelectedGroup.Value = CurveGroup.HueVsHue;
        model.IsPickingColor.Value = true;
        Assert.That(editor.Player.PointPick.Value, Is.Not.Null, "The preview waits for a click.");

        model.Effect.Value = second;
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(model.CanPickColor.Value, Is.True);
            Assert.That(model.IsPickingColor.Value, Is.False);
            Assert.That(editor.Player.PointPick.Value, Is.Null);
        });
    }

    // Shows the tab beside the preview and picks the orange with the tab's eyedropper.
    private static async Task<(Window Window, CurvesTabView View)> ShowAndPickAsync(EditViewModel editor, CurvesTabViewModel model)
    {
        var curvesView = new CurvesTabView { DataContext = model };
        var playerView = new PlayerView { DataContext = editor.Player };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(curvesView, 1);
        layout.Children.Add(playerView);
        layout.Children.Add(curvesView);
        var window = new Window { Content = layout, Width = 1000, Height = 500 };
        // The window's settings reach its controls, as the editor's do; the scrub test turns its pointer lock off.
        Beutl.Controls.ControlConfiguration.SetConfiguration(window, TestShell.Configuration);
        window.Show();
        HeadlessTestHelpers.Render(3);
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Render(2);
        HeadlessTestHelpers.Click(curvesView.FindControl<ToggleButton>("PickColorButton")!);
        AvaPoint left = OnFrame(playerView, window, 16, 16);
        window.MouseMove(left);
        window.MouseDown(left, MouseButton.Left);
        window.MouseUp(left, MouseButton.Left);
        await HeadlessTestHelpers.WaitUntilAsync(() => model.HasPickedColor.Value, message: "No color was picked.");
        HeadlessTestHelpers.Render(2);
        return (window, curvesView);
    }

    // Types the text into a box as a person would, then moves the focus away, which confirms it. Text set on the text
    // box itself would be a local value, which the box's template could no longer show over.
    private static TextBox TypeInto(Window window, CurvesTabView view, string box, string typed, InputElement next)
    {
        TextBox text = view.FindControl<NumberEditor<float>>(box)!.GetVisualDescendants().OfType<TextBox>().First();
        text.Focus();
        text.SelectAll();
        window.KeyTextInput(typed);
        HeadlessTestHelpers.Settle();
        next.Focus();
        HeadlessTestHelpers.Settle();
        return text;
    }

    private static CurveMap Level(float value) => new([new CurveControlPoint(0, value), new CurveControlPoint(1, value)]);

    private static RectShape CreateShape(Curves curves)
    {
        var fill = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        fill.GradientStops.Add(new GradientStop(s_left, 0));
        fill.GradientStops.Add(new GradientStop(s_left, 0.5f));
        fill.GradientStops.Add(new GradientStop(s_right, 0.5f));
        fill.GradientStops.Add(new GradientStop(s_right, 1));
        var shape = new RectShape { Width = { Value = FrameWidth }, Height = { Value = FrameHeight }, Fill = { Value = fill } };
        shape.FilterEffect.Value = curves;
        return shape;
    }

    // The point of the window over (x, y) of the frame.
    private static AvaPoint OnFrame(PlayerView view, Window window, double x, double y)
    {
        AvaPoint inImage = view.ViewportTransform!.ToPreview(new AvaPoint(x + 0.5, y + 0.5));
        return view.image.TranslatePoint(inImage, window)!.Value;
    }

    private static float HueOf(Color color)
        => CurveColorSample.FromLinear(Linear(color.R), Linear(color.G), Linear(color.B)).Hue;

    private static float Linear(byte value)
    {
        float c = value / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static int Chroma(Bgra8888 pixel)
        => Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
}

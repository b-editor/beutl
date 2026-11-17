using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
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

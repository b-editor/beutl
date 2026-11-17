using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.Views;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using AvaPoint = Avalonia.Point;
using Color = Beutl.Media.Color;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of the curves tab's eyedropper and color range beside the preview, for manual design review.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public sealed class CurvesTabCaptureTests
{
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;

    public enum Scene { Picking, Adjusted, AcrossRed }

    [AvaloniaTest]
    [TestCase(Scene.Picking)]
    [TestCase(Scene.Adjusted)]
    [TestCase(Scene.AcrossRed)]
    public async Task Capture_color_range(Scene scene)
    {
        TestGpu.RequireAnyDevice();
        ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"curves-capture-{scene}", FrameWidth, FrameHeight);
        var curves = new Curves();
        await TestShell.AddElementAsync(editor, () => CreateShape(curves), MediaTime.Zero, MediaTime.FromSeconds(2));

        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        model.SelectedGroup.Value = scene == Scene.Adjusted ? CurveGroup.HueVsSaturation : CurveGroup.HueVsHue;
        var curvesView = new CurvesTabView { DataContext = model };
        var playerView = new PlayerView { DataContext = editor.Player };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,420") };
        Grid.SetColumn(curvesView, 1);
        layout.Children.Add(playerView);
        layout.Children.Add(curvesView);
        var window = new Window { Content = layout, Width = 1180, Height = 540 };
        try
        {
            window.SetRenderScaling(2);
            window.Show();
            HeadlessTestHelpers.Render(3);
            await RenderThread.Dispatcher.InvokeAsync(static () => { });
            HeadlessTestHelpers.Render(3);

            // The orange stripe, or the red one, which straddles hue 0.
            AvaPoint target = OnFrame(playerView, window, scene == Scene.AcrossRed ? FrameWidth * 5 / 6 : FrameWidth / 6, FrameHeight / 2);
            HeadlessTestHelpers.Click(curvesView.FindControl<ToggleButton>("PickColorButton")!);
            window.MouseMove(target);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.HoverPosition.Value is not null, message: "No hover marker.");
            if (scene != Scene.Picking)
            {
                window.MouseDown(target, MouseButton.Left);
                window.MouseUp(target, MouseButton.Left);
                await HeadlessTestHelpers.WaitUntilAsync(() => model.HasPickedColor.Value, message: "No color was picked.");
                model.ConfirmRangeValue(scene == Scene.Adjusted ? 30 : 40);
                if (scene == Scene.AcrossRed)
                    model.ConfirmRangeWidth(90);
            }

            HeadlessTestHelpers.Render(3);
            await RenderThread.Dispatcher.InvokeAsync(static () => { });
            HeadlessTestHelpers.Render(3);
            ThemeCaptureTests.Capture(window, $"curves-color-range-{scene.ToString().ToLowerInvariant()}.png");
        }
        finally
        {
            curvesView.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Stripes of orange, green, blue and a red just past magenta, so that it lies across hue 0.
    private static RectShape CreateShape(Curves curves)
    {
        var fill = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        Color[] colors =
        [
            Color.FromArgb(255, 230, 110, 40), Color.FromArgb(255, 60, 170, 80),
            Color.FromArgb(255, 40, 90, 230), Color.FromArgb(255, 220, 30, 60),
        ];
        for (int i = 0; i < colors.Length; i++)
        {
            // The last stripe holds the red, two thirds of the frame on.
            float start = i == 3 ? 2 / 3f : i / 4.5f;
            float end = i == 3 ? 1 : (i + 1) / 4.5f;
            fill.GradientStops.Add(new GradientStop(colors[i], start));
            fill.GradientStops.Add(new GradientStop(colors[i], end));
        }

        var shape = new RectShape { Width = { Value = FrameWidth }, Height = { Value = FrameHeight }, Fill = { Value = fill } };
        shape.FilterEffect.Value = curves;
        return shape;
    }

    private static AvaPoint OnFrame(PlayerView view, Window window, double x, double y)
    {
        AvaPoint inImage = view.ViewportTransform!.ToPreview(new AvaPoint(x, y));
        return view.image.TranslatePoint(inImage, window)!.Value;
    }
}

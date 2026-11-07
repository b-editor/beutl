using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Controls.Styling.Themes;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.Views;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of the color grading tab for manual design review.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class ColorGradingTabCaptureTests
{
    public enum Placement { Auto, Above, Beside }

    [AvaloniaTest]
    [TestCase(1080, 360, 1, Placement.Auto, false, true)]
    [TestCase(1080, 360, 1, Placement.Above, false, true)]
    [TestCase(1080, 360, 0, Placement.Auto, false, true)]
    [TestCase(1080, 360, 0, Placement.Above, false, true)]
    [TestCase(1920, 360, 1, Placement.Auto, false, true)]
    [TestCase(1080, 360, 1, Placement.Auto, true, true)]
    [TestCase(1080, 360, 1, Placement.Auto, false, false)]
    [TestCase(1440, 480, 0, Placement.Auto, false, true)]
    [TestCase(560, 720, 1, Placement.Auto, false, true)]
    [TestCase(420, 900, 0, Placement.Auto, false, true)]
    public async Task Capture_color_grading_tab(int width, int height, int mode, Placement placement, bool light, bool adjustments)
    {
        ThemeCaptureTests.UseCaptureTheme(
            light ? ThemeVariant.Light : ThemeVariant.Dark,
            light ? null : BeutlDarkBorderTheme.AccentColor);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"grading-capture-{width}-{height}-{mode}");
        var grading = new ColorGrading();
        grading.Shadows.Value = new GradingColor(0.02f, 0.04f, 0.12f);
        grading.Highlights.Value = new GradingColor(0.1f, 0.06f, 0f);
        grading.Gain.Value = new GradingColor(1.1f, 1.02f, 0.95f);
        grading.Lift.Value = new GradingColor(-0.03f, 0f, 0.05f);
        grading.Contrast.Value = 12;
        grading.Temperature.Value = -18;
        grading.Exposure.Value = 0.35f;
        var shape = new RectShape();
        shape.FilterEffect.Value = grading;
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        using var model = new ColorGradingTabViewModel(editor);
        model.Effect.Value = grading;
        model.WheelMode.Value = model.AvailableWheelModes[mode];
        model.IsNumberEditorsVisible.Value = adjustments;
        var view = new ColorGradingTabView
        {
            DataContext = model,
            AdjustmentsBeside = placement switch { Placement.Above => false, Placement.Beside => true, _ => null },
        };
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            window.SetRenderScaling(2);
            window.Show();
            HeadlessTestHelpers.Render(5);
            string suffix = $"{(placement == Placement.Auto ? "" : "-" + placement.ToString().ToLowerInvariant())}{(light ? "-light" : "")}{(adjustments ? "" : "-wheels")}";
            ThemeCaptureTests.Capture(window, $"color-grading-{width}x{height}-{mode}{suffix}.png");
            ColorWheelsPanel wheels = view.GetVisualDescendants().OfType<ColorWheelsPanel>().Single(panel => panel.IsEffectivelyVisible);
            // Hidden sliders are never laid out, so their panel may not be in the tree.
            GradingAdjustmentsPanel? groups = view.GetVisualDescendants().OfType<GradingAdjustmentsPanel>().SingleOrDefault();
            TestContext.Out.WriteLine(
                $"{width}x{height} mode {mode} {placement}: beside={view.IsAdjustmentsBeside} wheels={wheels.Columns} " +
                $"side={wheels.IsSideBySide} pad={wheels.PadSize:F0} groups={groups?.UsedColumns.ToString() ?? "-"}");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Keyframes on some sliders, wheels and the low range, and an expression on Hue, with the playhead on the first
    // keyframe or between the two.
    [AvaloniaTest]
    [TestCase(1080, 360, 1, false)]
    [TestCase(1080, 360, 1, true)]
    [TestCase(1080, 360, 0, false)]
    [TestCase(560, 720, 1, false)]
    public async Task Capture_color_grading_tab_animated(int width, int height, int mode, bool atKeyFrame)
    {
        ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"grading-animated-{width}-{mode}-{atKeyFrame}");
        var grading = new ColorGrading();
        grading.Temperature.SetKeyFrameAnimation(Animate(-30f, 20f));
        grading.Exposure.SetKeyFrameAnimation(Animate(0f, 0.6f));
        grading.Gain.SetKeyFrameAnimation(Animate(GradingColor.One, new GradingColor(1.15f, 1f, 0.9f)));
        grading.Lift.SetKeyFrameAnimation(Animate(GradingColor.Zero, new GradingColor(-0.04f, 0f, 0.06f)));
        grading.Shadows.SetKeyFrameAnimation(Animate(GradingColor.Zero, new GradingColor(0.02f, 0.03f, 0.1f)));
        grading.LowRange.SetKeyFrameAnimation(Animate(30f, 50f));
        grading.Contrast.Value = 12;
        var shape = new RectShape();
        shape.FilterEffect.Value = grading;
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        editor.GetRequiredService<IEditorClock>().Seek(atKeyFrame ? MediaTime.Zero : MediaTime.FromSeconds(0.5));
        using var model = new ColorGradingTabViewModel(editor);
        model.Effect.Value = grading;
        model.WheelMode.Value = model.AvailableWheelModes[mode];
        Assert.That(((PropertyEditorViewModel)model.HueEditor.Value!).SetExpression("Sin(Time * 3) * 30", out string? error), Is.True, error);
        var window = new Window { Content = new ColorGradingTabView { DataContext = model }, Width = width, Height = height };
        try
        {
            window.SetRenderScaling(2);
            window.Show();
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(
                window, $"color-grading-animated-{width}x{height}-{mode}{(atKeyFrame ? "-at-keyframe" : "")}.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        static KeyFrameAnimation<T> Animate<T>(T from, T to)
        {
            var animation = new KeyFrameAnimation<T>();
            animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = from } });
            animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = to } });
            return animation;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Components.AudioVisualizerTab;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.AudioVisualizerTab.Views;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.Views;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.Views;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.Views;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SelectorToolTabBarTests
{
    [AvaloniaTest]
    public void Trailing_controls_wrap_right_without_moving_the_leading_title()
    {
        var title = new Border { Width = 80, Height = 32 };
        var action = new Border { Width = 24, Height = 32 };
        var selector = new Border { Width = 160, Height = 32 };
        var panel = new ToolTabBarPanel { Children = { title, action, selector } };
        Arrange(300);
        Assert.That(title.Bounds.Left, Is.Zero);
        Assert.That(selector.Bounds.Right, Is.EqualTo(300));
        Assert.That(selector.Bounds.Top, Is.EqualTo(title.Bounds.Top));

        Arrange(200);
        Assert.That(title.Bounds.Left, Is.Zero);
        Assert.That(action.Bounds.Right, Is.EqualTo(200));
        Assert.That(selector.Bounds.Right, Is.EqualTo(200));
        Assert.That(selector.Bounds.Top, Is.GreaterThan(title.Bounds.Bottom));

        action.IsVisible = false;
        Arrange(260);
        Assert.That(selector.Bounds.Top, Is.EqualTo(title.Bounds.Top));
        Assert.That(selector.Bounds.Right, Is.EqualTo(260));

        void Arrange(double width)
        {
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Selectors_and_settings_remain_usable_at_narrow_widths(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"selector-bars-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "selector-bars", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using var curves = new CurvesTabViewModel(editor);
        using var grading = new ColorGradingTabViewModel(editor);
        using var scopes = new ColorScopesTabViewModel(editor);
        using var audio = new AudioVisualizerTabViewModel(editor, AudioVisualizerTabExtension.Instance);
        var curvesView = new CurvesTabView { DataContext = curves };
        var gradingView = new ColorGradingTabView { DataContext = grading };
        var scopesView = new ColorScopesTabView { DataContext = scopes };
        var audioView = new AudioVisualizerTabView { DataContext = audio };
        var window = new Window
        {
            Width = width,
            Height = 360,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Content = curvesView;
            window.Show();
            ComboBox channel = curvesView.FindControl<ComboBox>("ChannelComboBox")!;
            ComboBox group = curvesView.FindControl<ComboBox>("GroupComboBox")!;
            CheckBar(curvesView, "curves-custom");
            channel.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(curves.SelectedChannelItem.Value, Is.SameAs(curves.CustomCurveChannels[1]));
            group.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(curves.SelectedGroupItem.Value, Is.SameAs(curves.CurveGroups[1]));
            Assert.That(channel.IsEffectivelyVisible, Is.False);
            CheckBar(curvesView, "curves-hue");

            window.Content = gradingView;
            CheckBar(gradingView, "grading-shadows");
            gradingView.FindControl<ComboBox>("WheelModeComboBox")!.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(grading.WheelMode.Value, Is.EqualTo(ColorGradingWheelMode.LiftGammaGainOffset));
            CheckBar(gradingView, "grading-lift");
            Click(gradingView.FindControl<ToggleButton>("NumberEditorsButton")!);
            Assert.That(grading.IsNumberEditorsVisible.Value, Is.False);

            window.Content = scopesView;
            CheckBar(scopesView, "scopes-waveform");
            scopesView.FindControl<ComboBox>("WaveformModeComboBox")!.SelectedIndex = 0;
            scopesView.FindControl<ComboBox>("ColorSpaceComboBox")!.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(scopes.WaveformMode.Value, Is.EqualTo(WaveformMode.Luma));
            Assert.That(scopes.ColorSpace.Value, Is.EqualTo(ScopeColorSpace.Linear));
            scopesView.FindControl<ComboBox>("ScopeTypeComboBox")!.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(scopes.SelectedScopeType.Value, Is.EqualTo(ColorScopeType.Histogram));
            Assert.That(scopesView.FindControl<ComboBox>("WaveformModeComboBox")!.IsEffectivelyVisible, Is.False);
            Assert.That(scopesView.FindControl<ComboBox>("HistogramModeComboBox")!.IsEffectivelyVisible, Is.True);
            CheckBar(scopesView, "scopes-histogram");
            scopesView.FindControl<ComboBox>("ScopeTypeComboBox")!.SelectedIndex = 4;
            HeadlessTestHelpers.Settle();
            Button zebraSettings = scopesView.FindControl<Button>("ZebraSettingsButton")!;
            CheckBar(scopesView, "scopes-zebra");
            Click(zebraSettings);
            Assert.That(zebraSettings.Flyout!.IsOpen, Is.True);
            var zebraContent = (Control)((Flyout)zebraSettings.Flyout).Content!;
            zebraContent.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 0.75m;
            HeadlessTestHelpers.Settle();
            Assert.That(scopes.ZebraHighThreshold.Value, Is.EqualTo(0.75f));
            zebraSettings.Flyout.Hide();

            window.Content = audioView;
            CheckBar(audioView, "audio-waveform");
            audioView.FindControl<ComboBox>("ModeComboBox")!.SelectedIndex = 1;
            HeadlessTestHelpers.Settle();
            Assert.That(audio.SelectedMode.Value, Is.EqualTo(AudioVisualizerMode.Spectrum));
            CheckBar(audioView, "audio-spectrum");
            Button audioSettings = audioView.FindControl<Button>("SettingsButton")!;
            Click(audioSettings);
            Assert.That(audioSettings.Flyout!.IsOpen, Is.True);
            var audioContent = (Control)((Flyout)audioSettings.Flyout).Content!;
            audioContent.GetLogicalDescendants().OfType<ComboBox>().First().SelectedItem = 1024;
            HeadlessTestHelpers.Settle();
            Assert.That(audio.FftSize.Value, Is.EqualTo(1024));
            audioSettings.Flyout.Hide();
        }
        finally { window.Close(); }

        void Click(Control control)
        {
            Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
        }

        void CheckBar(Control view, string name)
        {
            window.UpdateLayout();
            HeadlessTestHelpers.Render();
            ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
            Assert.That(bar.Bounds.Height, Is.GreaterThanOrEqualTo(bar.MinHeight), name);
            foreach (Control control in bar.GetLogicalDescendants().OfType<Control>()
                         .Where(c => c.IsEffectivelyVisible && c is ComboBox or Button))
            {
                Point position = control.TranslatePoint(default, bar)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(control.Bounds.Width, Is.GreaterThan(0), name);
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(position.Y, Is.GreaterThanOrEqualTo(0), name);
                    Assert.That(position.X + control.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width), name);
                    Assert.That(position.Y + control.Bounds.Height, Is.LessThanOrEqualTo(bar.Bounds.Height), name);
                });
            }
            if (Environment.GetEnvironmentVariable("BEUTL_SELECTOR_BAR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}

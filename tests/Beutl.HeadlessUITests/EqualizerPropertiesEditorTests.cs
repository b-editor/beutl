using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.EqualizerProperties.ViewModels;
using Beutl.Editor.Components.EqualizerProperties.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EqualizerPropertiesEditorTests
{
    private static PropertyEditorExtension[] Extensions => LoadPrimitiveExtensionTask.PrimitiveExtensions
        .OfType<PropertyEditorExtension>().ToArray();

    [AvaloniaTest]
    public void BandCountEditorComesBeforeTheCurve()
    {
        using var host = new EqualizerHost(400);
        ComboBox bandCount = host.Editor.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.That(bandCount.TranslatePoint(default, host.Editor)!.Value.Y,
            Is.LessThan(host.Curve.TranslatePoint(default, host.Editor)!.Value.Y));
    }

    [AvaloniaTest]
    [TestCase(280)]
    [TestCase(480)]
    [TestCase(760)]
    public void SelectedBandEditorsAlignWithTheBandCountEditor(int width)
    {
        using var host = new EqualizerHost(width);
        host.ViewModel.SelectedBandIndex.Value = 3;
        HeadlessTestHelpers.Render(3);
        Control[] inputs =
        [
            .. host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>()
                .Select(number => number.GetVisualDescendants().OfType<TextBox>().Single()),
            .. host.Editor.GetVisualDescendants().OfType<ComboBox>()
        ];
        Assert.That(inputs, Has.Length.EqualTo(5), "Frequency, gain, Q, filter type and band count.");
        foreach (Control input in inputs)
        {
            Assert.That(input.TranslatePoint(default, host.Editor)!.Value.X,
                Is.EqualTo(inputs[0].TranslatePoint(default, host.Editor)!.Value.X).Within(0.01));
            Assert.That(input.Bounds.Width, Is.EqualTo(inputs[0].Bounds.Width).Within(0.01));
        }
    }

    [AvaloniaTest]
    public void DraggingAHandleToTheGridCornersReachesTheRangeLimits()
    {
        using var host = new EqualizerHost(400);
        EqualizerBandItemViewModel band = host.ViewModel.Bands[5];
        Rect grid = host.Curve.PlotBounds;

        host.Drag(host.Curve.GetHandleCenter(band), grid.TopRight);
        Assert.That(band.Band.Frequency.Value, Is.EqualTo(EqualizerCurveEditor.MaxFrequency).Within(0.5));
        Assert.That(band.Band.Gain.Value, Is.EqualTo(EqualizerCurveEditor.MaxGain).Within(0.01));

        host.Drag(host.Curve.GetHandleCenter(band), grid.BottomLeft);
        Assert.That(band.Band.Frequency.Value, Is.EqualTo(EqualizerCurveEditor.MinFrequency).Within(0.01));
        Assert.That(band.Band.Gain.Value, Is.EqualTo(EqualizerCurveEditor.MinGain).Within(0.01));
    }

    [AvaloniaTest]
    public void PlaybackReusesTheCurveUntilTheEffectiveGainChanges()
    {
        using var host = new EqualizerHost(400);
        var flat = host.Curve.ResponseCurve!;
        Assert.That(flat, Is.Not.Null);

        host.ViewModel.CurrentTime.Value = MediaTime.FromSeconds(1);
        host.ViewModel.SelectedBandIndex.Value = 3;
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.ResponseCurve, Is.SameAs(flat));

        host.Equalizer.Bands[3].Gain.SetKeyFrameAnimation(new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 0 } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 12 } }
            }
        });
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.ResponseCurve, Is.SameAs(flat));

        host.ViewModel.CurrentTime.Value = MediaTime.FromSeconds(2);
        HeadlessTestHelpers.Render();
        var peak = host.Curve.ResponseCurve!;
        Assert.That(peak.Bounds.Top, Is.LessThan(flat.Bounds.Top));

        host.ViewModel.CurrentTime.Value = MediaTime.FromSeconds(3);
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.ResponseCurve, Is.SameAs(peak));
    }

    [AvaloniaTest]
    public void PausedEditsSampleRateAndResizeUpdateTheCurve()
    {
        using var host = new EqualizerHost(400);
        var band = host.Equalizer.Bands[5];
        void CheckRebuild(Action change)
        {
            var previous = host.Curve.ResponseCurve;
            change();
            HeadlessTestHelpers.Render();
            Assert.That(host.Curve.ResponseCurve, Is.Not.Null.And.Not.SameAs(previous));
        }

        CheckRebuild(() => band.Gain.Value = 9);
        CheckRebuild(() => band.Frequency.Value = 6000);
        CheckRebuild(() => band.Q.Value = 3);
        CheckRebuild(() => band.FilterType.Value = BiQuadFilterType.HighShelf);
        CheckRebuild(() => host.ViewModel.SampleRate.Value = 48000);
        double width = host.Curve.PlotBounds.Width;
        CheckRebuild(() => host.Editor.Width = 280);
        Assert.That(host.Curve.ResponseCurve!.Bounds.Width, Is.LessThan(width));

        CheckRebuild(() => band.IsEnabled.Value = false);
        var disabled = host.Curve.ResponseCurve!;
        Assert.That(disabled.Bounds.Height, Is.EqualTo(0).Within(0.01));
        band.Gain.Value = 12;
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.ResponseCurve, Is.SameAs(disabled));
        CheckRebuild(() => band.IsEnabled.Value = true);
        Assert.That(host.Curve.ResponseCurve!.Bounds.Height, Is.GreaterThan(1));

        host.Equalizer.Bands.Clear();
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.ResponseCurve, Is.Null);
        CheckRebuild(() => host.Equalizer.BandCountOption.Value = BandCountPreset.Bands5);
    }

    // The curve editor paints with the theme, not a fixed dark palette, and repaints when the theme changes.
    [AvaloniaTest]
    public void CurvePaintsWithTheTheme()
    {
        using var host = new EqualizerHost(400);
        host.Equalizer.Bands[3].Gain.Value = 9;
        host.Equalizer.Bands[7].Gain.Value = -6;
        host.ViewModel.SelectedBandIndex.Value = 3;
        using var scope = Assert.EnterMultipleScope();
        foreach (ThemeVariant theme in ThemePixels.Themes)
        {
            host.Window.RequestedThemeVariant = theme;
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(host.Window, nameof(EqualizerPropertiesEditorTests), $"theme-{theme}");
            Rect plot = host.Curve.PlotBounds;
            // Between the 200 and 500 Hz lines and the +12 and +18 dB lines, clear of the curve.
            ThemePixels.AssertBrushOverWindow(host.Curve,
                new Point(plot.X + plot.Width * 0.4, plot.Y + plot.Height * 0.19), "ControlFillColorDefaultBrush");
            ThemePixels.AssertBrushOverWindow(host.Curve, host.Curve.GetHandleCenter(host.ViewModel.Bands[3]),
                "AccentFillColorDefaultBrush");
        }
    }

    private sealed class EqualizerHost : IDisposable
    {
        private readonly List<IPropertyEditorContext> _contexts = [];

        public EqualizerHost(int width)
        {
            PropertyEditorResolver.CreateContexts(PropertyAdapterFactory.CreateAdapters(Equalizer), Extensions,
                PropertyEditorSurface.Panel, _contexts.Add);
            ViewModel = _contexts.OfType<EqualizerPropertiesViewModel>().Single();
            var factory = new Mock<IPropertyEditorFactory>();
            factory.Setup(f => f.CreateEditor(It.IsAny<IPropertyAdapter>())).Returns((IPropertyAdapter p) =>
                PropertyEditorResolver.CreateContext(p, Extensions, PropertyEditorSurface.Panel));
            ViewModel.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IPropertyEditorFactory), factory.Object)));
            Assert.That(PropertyEditorHost.TryCreateControl(ViewModel, PropertyEditorSurface.Panel, out var control), Is.True);
            Editor = (EqualizerPropertiesEditor)control!;
            Editor.DataContext = ViewModel;
            PropertyEditorGrid.SetIsAlignmentScope(Editor, true);
            Window = new Window
            {
                Content = new Border { Padding = new Thickness(12), Child = Editor },
                Width = width,
                Height = 720,
                RequestedThemeVariant = ThemeVariant.Dark
            };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Curve = Editor.GetVisualDescendants().OfType<EqualizerCurveEditor>().Single();
        }

        public EqualizerEffect Equalizer { get; } = new();
        public EqualizerPropertiesViewModel ViewModel { get; }
        public EqualizerPropertiesEditor Editor { get; }
        public EqualizerCurveEditor Curve { get; }
        public Window Window { get; }

        // Points are in the curve editor's coordinates.
        public void Drag(Point from, Point to)
        {
            Point start = Curve.TranslatePoint(from, Window)!.Value;
            Point end = Curve.TranslatePoint(to, Window)!.Value;
            Window.MouseMove(start);
            Window.MouseDown(start, MouseButton.Left);
            Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Window.Close();
            foreach (var context in _contexts) context.Dispose();
        }
    }
}

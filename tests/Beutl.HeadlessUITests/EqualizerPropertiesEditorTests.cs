using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Audio.Effects;
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

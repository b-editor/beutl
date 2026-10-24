using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.Threading;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaColors = Avalonia.Media.Colors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BrushEditorGradientColorTests
{
    [AvaloniaTest]
    public void Switching_from_solid_to_gradient_shows_the_first_stops_opacity()
    {
        var shape = new RectShape { Fill = { Value = new SolidColorBrush(Colors.Red) } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new BrushEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 480, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            BrushEditorFlyoutPresenter presenter = window.GetVisualDescendants().OfType<BrushEditorFlyoutPresenter>().Single();
            ToggleButton gradientTab = presenter.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "GradientBrushTabButton");

            HeadlessTestHelpers.Click(gradientTab);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            RenderThread.Dispatcher.Invoke(static () => { }, DispatchPriority.Low, timeout.Token);
            HeadlessTestHelpers.Render(3);

            if (Environment.GetEnvironmentVariable("BEUTL_THEME_CAPTURE_DIR") is not null)
                ThemeCaptureTests.Capture(window, "brush-gradient-opacity.png");

            var brush = (LinearGradientBrush)shape.Fill.Value!;
            var picker = (SimpleColorPicker)presenter.Content!;
            TextBox opacity = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "OpacityBox");
            Assert.Multiple(() =>
            {
                Assert.That(brush.GradientStops[0].Color.Value, Is.EqualTo(Colors.White));
                Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(AvaColors.White));
                Assert.That(opacity.Text, Is.EqualTo(1f.ToString("P0")));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Picker_follows_only_the_selected_stops_color_without_reporting_an_edit()
    {
        var first = new Avalonia.Media.GradientStop(AvaColors.White, 0);
        var second = new Avalonia.Media.GradientStop(AvaColors.Black, 1);
        var picker = new SimpleColorPicker();
        var presenter = new BrushEditorFlyoutPresenter
        {
            Content = picker,
            Brush = new Avalonia.Media.LinearGradientBrush { GradientStops = [first, second] },
            OriginalBrush = new LinearGradientBrush()
        };
        var window = new Window { Content = presenter, Width = 280, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            GradientStopsSlider slider = presenter.GetVisualDescendants().OfType<GradientStopsSlider>().Single();
            int edits = 0;
            presenter.GradientStopChanged += (_, _) => edits++;
            presenter.GradientStopConfirmed += (_, _) => edits++;

            first.Color = Avalonia.Media.Color.FromArgb(128, 255, 0, 0);
            Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(first.Color));
            TextBox opacity = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "OpacityBox");
            Assert.That(opacity.Text, Is.EqualTo((128f / 255).ToString("P0")));

            slider.SelectedStop = second;
            first.Color = AvaColors.Blue;
            Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(second.Color));
            second.Color = AvaColors.Green;
            Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(second.Color));
            Assert.That(edits, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }
}

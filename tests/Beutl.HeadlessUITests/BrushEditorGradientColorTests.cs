using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.Threading;
using FluentAvalonia.UI.Media;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaColors = Avalonia.Media.Colors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BrushEditorGradientColorTests
{
    [Test]
    public void Changing_the_type_of_a_gradient_keeps_how_it_mixes()
    {
        var linear = new LinearGradientBrush
        {
            InterpolationSpace = { Value = GradientInterpolationSpace.Oklab },
            InterpolatePremultiplied = { Value = true },
        };

        GradientBrush radial = BrushEditor.CreateGradientBrush(BrushType.RadialGradientBrush, linear);

        Assert.Multiple(() =>
        {
            Assert.That(radial.InterpolationSpace.Value, Is.EqualTo(GradientInterpolationSpace.Oklab));
            Assert.That(radial.InterpolatePremultiplied.Value, Is.True);
        });
    }

    // Black to white, sampled halfway: linear sRGB is the middle of the light, and sRGB the middle of the bytes.
    [AvaloniaTest]
    [TestCase(GradientInterpolationSpace.LinearSrgb, 188)]
    [TestCase(GradientInterpolationSpace.Srgb, 128)]
    [TestCase(GradientInterpolationSpace.Oklab, 99)]
    public void The_swatch_and_the_stop_bar_show_how_the_gradient_mixes(GradientInterpolationSpace space, int middle)
    {
        var gradient = new LinearGradientBrush { InterpolationSpace = { Value = space } };
        gradient.GradientStops.AddRange([new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1)]);
        var shape = new RectShape { Fill = { Value = gradient } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new BrushEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 480, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            GradientStopsSlider slider = window.GetVisualDescendants().OfType<GradientStopsSlider>().Single();
            HeadlessCapture.SaveIfRequested(window, nameof(BrushEditorGradientColorTests), $"mixes-in-{space}");

            var swatch = (Avalonia.Media.LinearGradientBrush)viewModel.SwatchBrush.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(ColorAt(swatch.GradientStops, 0.5).R, Is.EqualTo(middle).Within(1));
                Assert.That(ColorAt(slider.BackgroundStops!, 0.5).R, Is.EqualTo(middle).Within(1));
                Assert.That(slider.Stops, Has.Count.EqualTo(2), "The handles stay on the stops themselves.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_swatch_follows_what_changes_how_the_gradient_looks()
    {
        var stop = new GradientStop(Colors.Black, 0);
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.AddRange([stop, new GradientStop(Colors.White, 1)]);
        var shape = new RectShape { Fill = { Value = gradient } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        SettleEngineChange();
        var swatch = (Avalonia.Media.LinearGradientBrush)viewModel.SwatchBrush.Value!;
        Assert.That(ColorAt(swatch.GradientStops, 0.5).R, Is.EqualTo(188).Within(1));

        gradient.InterpolationSpace.Value = GradientInterpolationSpace.Srgb;
        SettleEngineChange();
        Assert.That(ColorAt(swatch.GradientStops, 0.5).R, Is.EqualTo(128).Within(1));

        stop.Color.Value = Colors.White;
        SettleEngineChange();
        Assert.That(ColorAt(swatch.GradientStops, 0.5).R, Is.EqualTo(255));

        stop.Offset.Value = 0.75f;
        stop.Color.Value = Colors.Black;
        SettleEngineChange();
        Assert.That(ColorAt(swatch.GradientStops, 0.875).R, Is.EqualTo(128).Within(1));
    }

    // A change reaches the swatch through the render thread.
    private static void SettleEngineChange()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        RenderThread.Dispatcher.Invoke(static () => { }, DispatchPriority.Low, timeout.Token);
        HeadlessTestHelpers.Render(3);
    }

    [AvaloniaTest]
    public void A_solid_brush_shows_in_its_swatch_as_it_is()
    {
        var shape = new RectShape { Fill = { Value = new SolidColorBrush(Colors.Red) } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));

        Assert.That(viewModel.SwatchBrush.Value, Is.SameAs(viewModel.AvaloniaBrush.Value));
        Assert.That(viewModel.DisplayStops.Value, Is.Null);
    }

    [AvaloniaTest]
    public void The_stop_bar_of_a_brushs_stops_shows_how_the_brush_mixes()
    {
        var gradient = new LinearGradientBrush { InterpolationSpace = { Value = GradientInterpolationSpace.Srgb } };
        gradient.GradientStops.AddRange([new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1)]);
        var shape = new RectShape { Fill = { Value = gradient } };
        using var history = new HistoryScope(shape);
        (PropertyEditorViewModel viewModel, Control editor) = CreateControl(
            new EnginePropertyAdapter<ICoreList<GradientStop>>(gradient.GradientStops, gradient));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GradientStopsSlider slider = editor.GetVisualDescendants().OfType<GradientStopsSlider>().Single();

            Assert.That(ColorAt(slider.BackgroundStops!, 0.5).R, Is.EqualTo(128).Within(1));
            Assert.That(slider.Stops, Has.Count.EqualTo(2));
        }
        finally
        {
            window.Close();
        }
    }

    // Avalonia mixes these stops in sRGB, and they stand close enough for that to pass for any space.
    private static Avalonia.Media.Color ColorAt(Avalonia.Media.GradientStops stops, double offset)
    {
        for (int i = 1; i < stops.Count; i++)
        {
            Avalonia.Media.GradientStop previous = stops[i - 1];
            Avalonia.Media.GradientStop next = stops[i];
            if (offset > next.Offset) continue;

            double t = next.Offset > previous.Offset ? (offset - previous.Offset) / (next.Offset - previous.Offset) : 0;
            byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * t);
            return Avalonia.Media.Color.FromArgb(
                Mix(previous.Color.A, next.Color.A),
                Mix(previous.Color.R, next.Color.R),
                Mix(previous.Color.G, next.Color.G),
                Mix(previous.Color.B, next.Color.B));
        }

        return stops[^1].Color;
    }

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

    [AvaloniaTest]
    [TestCase(0, 50)]
    [TestCase(100, 0)]
    public void Typing_an_achromatic_gradient_color_preserves_hsv_state(int saturation, int value)
    {
        var stop = new Avalonia.Media.GradientStop(Color2.FromHSV(240, 100, 50), 0);
        var picker = new SimpleColorPicker { InputType = SimpleColorPickerInputType.Hsv };
        var presenter = new BrushEditorFlyoutPresenter
        {
            Content = picker,
            Brush = new Avalonia.Media.LinearGradientBrush { GradientStops = [stop] },
            OriginalBrush = new LinearGradientBrush()
        };
        var window = new Window { Content = presenter, Width = 280, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            string name = saturation == 0 ? "PART_InnerSecondTextBox" : "PART_InnerThirdTextBox";
            TextBox component = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == name);
            Assert.That(component.Focus(), Is.True);
            component.SelectAll();
            window.KeyTextInput("0");
            HeadlessTestHelpers.Render();

            // A later edit must retain the hue (and saturation for black) chosen before the color became achromatic.
            TextBox opacity = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "OpacityBox");
            Assert.That(opacity.Focus(), Is.True);
            opacity.SelectAll();
            window.KeyTextInput("50%");
            HeadlessTestHelpers.Render();
            if (Environment.GetEnvironmentVariable("BEUTL_THEME_CAPTURE_DIR") is not null)
                ThemeCaptureTests.Capture(window, $"brush-gradient-hsv-{saturation}-{value}.png");

            Assert.Multiple(() =>
            {
                Assert.That(picker.IsDragging, Is.False);
                Assert.That(picker.Color.Hue, Is.EqualTo(240));
                Assert.That(picker.Color.Saturation, Is.EqualTo(saturation));
                Assert.That(picker.Color.Value, Is.EqualTo(value));
                Assert.That(picker.Color.A, Is.EqualTo(128));
                Assert.That(stop.Color, Is.EqualTo((Avalonia.Media.Color)Color2.FromHSV(240, saturation, value, 128)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Gradient_color_drag_ignores_preview_updates_and_cancellation_restores_the_picker()
    {
        var red = new GradientStop(Colors.Red, 0);
        var brush = new LinearGradientBrush();
        brush.GradientStops.Add(red);
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var shape = new RectShape { Fill = { Value = brush } };
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
            HeadlessTestHelpers.Render(3);
            BrushEditorFlyoutPresenter presenter = window.GetVisualDescendants().OfType<BrushEditorFlyoutPresenter>().Single();
            var picker = (SimpleColorPicker)presenter.Content!;
            await HeadlessTestHelpers.WaitUntilAsync(() => (Avalonia.Media.Color)picker.Color == AvaColors.Red);
            ColorSlider hue = picker.GetVisualDescendants().OfType<ColorSlider>()
                .Single(slider => slider.Name == "ThirdComponentSlider");
            Thumb thumb = hue.GetVisualDescendants().OfType<Thumb>().Single();
            Point start = HeadlessTestHelpers.Center(thumb, window);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(100, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            var draggedColor = (Avalonia.Media.Color)picker.Color;
            Color reached = red.Color.Value;
            Assert.Multiple(() =>
            {
                Assert.That(picker.IsDragging, Is.True);
                Assert.That(reached, Is.Not.EqualTo(Colors.Red));
                Assert.That(draggedColor, Is.Not.EqualTo(AvaColors.Blue));
                Assert.That(history.History.IsEditing, Is.True);
            });

            GradientStopsSlider stops = presenter.GetVisualDescendants().OfType<GradientStopsSlider>().Single();
            stops.SelectedStop!.Color = AvaColors.Blue;
            Assert.Multiple(() =>
            {
                Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(draggedColor));
                Assert.That(red.Color.Value, Is.EqualTo(reached));
            });

            Assert.That(history.History.TryCancelGesture(), Is.True);
            Assert.That(picker.IsDragging, Is.False);
            await HeadlessTestHelpers.WaitUntilAsync(() => (Avalonia.Media.Color)picker.Color == AvaColors.Red);
            window.MouseMove(start + new Vector(140, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(140, 0), MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(red.Color.Value, Is.EqualTo(Colors.Red));
                Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(AvaColors.Red));
                Assert.That(history.History.IsEditing, Is.False);
                Assert.That(history.History.UndoCount, Is.Zero);
            });
        }
        finally
        {
            window.Close();
        }
    }
}

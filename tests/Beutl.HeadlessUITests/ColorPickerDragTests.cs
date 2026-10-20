using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using FluentAvalonia.UI.Media;
using Moq;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaColors = Avalonia.Media.Colors;
using ColorSpectrum = Avalonia.Controls.Primitives.ColorSpectrum;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ColorPickerDragTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    // Avalonia's spectrum maps points to colors only once it has built its color map in the background.
    private static Task WaitForColorMapAsync(ColorSpectrum spectrum)
    {
        FieldInfo map = typeof(ColorSpectrum).GetField("_hsvValues", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new AssertionException("ColorSpectrum no longer keeps its color map in _hsvValues.");
        return HeadlessTestHelpers.WaitUntilAsync(() => ((ICollection)map.GetValue(spectrum)!).Count > 0);
    }

    // Avalonia's spectrum keeps its pressed state after losing the capture; the picker must not let hovering recolor it.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_spectrum_press_that_lost_capture_or_was_stopped_ignores_hovering(bool stop)
    {
        var picker = new SimpleColorPicker { Color = AvaColors.Red };
        int confirmed = 0;
        picker.ColorConfirmed += (_, _) => confirmed++;
        var window = new Window { Content = picker, Width = 400, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ColorSpectrum spectrum = picker.GetVisualDescendants().OfType<ColorSpectrum>().First(s => s.IsEffectivelyVisible);
            await WaitForColorMapAsync(spectrum);
            IPointer? pointer = null;
            spectrum.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Point At(double x, double y) => spectrum.TranslatePoint(
                new Point(x * spectrum.Bounds.Width, y * spectrum.Bounds.Height), window)!.Value;

            window.MouseMove(At(0.3, 0.3));
            window.MouseDown(At(0.3, 0.3), MouseButton.Left);
            window.MouseMove(At(0.6, 0.6), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            Assert.That(picker.IsDragging, Is.True);
            Color2 reached = picker.Color;

            if (stop)
                picker.StopDrag();
            else
                pointer!.Capture(null);

            window.MouseMove(At(0.8, 0.2));
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(picker.IsDragging, Is.False);
                Assert.That(picker.Color, Is.EqualTo(reached));
                Assert.That(confirmed, Is.EqualTo(stop ? 0 : 1), "Capture loss confirms as release does; StopDrag does not.");
            });

            window.MouseUp(At(0.8, 0.2), MouseButton.Left);
            // The next press drags again.
            window.MouseMove(At(0.2, 0.8));
            window.MouseDown(At(0.2, 0.8), MouseButton.Left);
            window.MouseMove(At(0.4, 0.7), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(0.4, 0.7), MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(picker.Color, Is.Not.EqualTo(reached));
                Assert.That(confirmed, Is.EqualTo(stop ? 1 : 2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public async Task A_solid_color_drag_in_the_brush_flyout_keeps_on_capture_loss_and_playback_and_reverts_on_Escape_and_Undo(
        DragEnd end)
    {
        var brush = new SolidColorBrush(Colors.Red);
        var shape = new RectShape { Fill = { CurrentValue = brush } };
        using var history = new HistoryScope(shape);
        var playing = new ReactivePropertySlim<bool>(false);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(playing);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(new ServiceVisitor((typeof(HistoryManager), history.History), (typeof(IPreviewPlayer), player.Object)));
        var editor = new BrushEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 480, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            ColorSpectrum spectrum = window.GetVisualDescendants().OfType<ColorSpectrum>().First(s => s.IsEffectivelyVisible);
            await WaitForColorMapAsync(spectrum);
            IPointer? pointer = null;
            spectrum.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Point At(double x, double y) => spectrum.TranslatePoint(
                new Point(x * spectrum.Bounds.Width, y * spectrum.Bounds.Height), window)!.Value;

            window.MouseMove(At(0.3, 0.3));
            window.MouseDown(At(0.3, 0.3), MouseButton.Left);
            window.MouseMove(At(0.6, 0.6), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            Color reached = brush.Color.CurrentValue;
            Assert.That(reached, Is.Not.EqualTo(Colors.Red));
            Assert.That(history.History.IsEditing, Is.True, "The drag must hold an edit session.");

            switch (end)
            {
                case DragEnd.CaptureLoss:
                    pointer!.Capture(null);
                    break;
                case DragEnd.Playback:
                    playing.Value = true;
                    playing.Value = false;
                    break;
                case DragEnd.Escape:
                    window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    break;
                case DragEnd.Undo:
                    Assert.That(history.History.TryCancelGesture(), Is.True);
                    break;
            }

            HeadlessTestHelpers.Render();
            // Neither a further move nor the release recolors the brush.
            window.MouseMove(At(0.8, 0.2), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(0.8, 0.2), MouseButton.Left);
            HeadlessTestHelpers.Render();

            bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
            Color2 picked = window.GetVisualDescendants().OfType<SimpleColorPicker>().Single().Color;
            Color shown = Color.FromArgb(picked.A, picked.R, picked.G, picked.B);
            Assert.Multiple(() =>
            {
                Assert.That(brush.Color.CurrentValue, Is.EqualTo(kept ? reached : Colors.Red));
                Assert.That(shown, Is.EqualTo(brush.Color.CurrentValue), "The picker shows the brush's color.");
                Assert.That(history.History.UndoCount, Is.EqualTo(kept ? 1 : 0));
                Assert.That(history.History.IsEditing, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void A_grading_wheel_drag_keeps_on_capture_loss_and_playback_and_reverts_on_Escape_and_Undo(DragEnd end)
    {
        var effect = new ColorGrading();
        using var history = new HistoryScope(effect);
        var playing = new ReactivePropertySlim<bool>(false);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(playing);
        using var viewModel = new GradingColorEditorViewModel(
            new AnimatablePropertyAdapter<GradingColor>((AnimatableProperty<GradingColor>)effect.Shadows, effect));
        viewModel.Accept(new ServiceVisitor((typeof(HistoryManager), history.History), (typeof(IPreviewPlayer), player.Object)));
        var wheel = new ColorGradingWheel();
        viewModel.Accept(wheel);
        var window = new Window { Content = wheel, Width = 300, Height = 500 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GradingWheel thumb = wheel.GetVisualDescendants().OfType<GradingWheel>().First(w => w.IsEffectivelyVisible);
            IPointer? pointer = null;
            thumb.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Point start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            GradingColor reached = effect.Shadows.CurrentValue;
            Assert.That(reached, Is.Not.EqualTo(GradingColor.Zero));
            Assert.That(history.History.IsEditing, Is.True, "The drag must hold an edit session.");

            switch (end)
            {
                case DragEnd.CaptureLoss:
                    pointer!.Capture(null);
                    break;
                case DragEnd.Playback:
                    playing.Value = true;
                    playing.Value = false;
                    break;
                case DragEnd.Escape:
                    window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    break;
                case DragEnd.Undo:
                    Assert.That(history.History.TryCancelGesture(), Is.True);
                    break;
            }

            HeadlessTestHelpers.Render();
            window.MouseMove(start + new Vector(80, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(80, 0), MouseButton.Left);
            HeadlessTestHelpers.Render();

            bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
            Assert.Multiple(() =>
            {
                Assert.That(effect.Shadows.CurrentValue, Is.EqualTo(kept ? reached : GradingColor.Zero));
                Assert.That(history.History.UndoCount, Is.EqualTo(kept ? 1 : 0));
                Assert.That(history.History.IsEditing, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }
}

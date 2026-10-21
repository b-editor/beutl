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
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using Moq;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class GradientStopDragTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void Capture_loss_and_playback_keep_a_stop_drag_and_Escape_and_Undo_revert_it(DragEnd end)
    {
        var brush = new LinearGradientBrush();
        var red = new GradientStop(Colors.Red, 0f);
        brush.GradientStops.Add(red);
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1f));
        var shape = new RectShape { Fill = { CurrentValue = brush } };
        using var history = new HistoryScope(shape);
        var playing = new ReactivePropertySlim<bool>(false);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(playing);
        (BaseEditorViewModel viewModel, Control editor) = CreateControl(
            new EnginePropertyAdapter<ICoreList<GradientStop>>(brush.GradientStops, brush));
        viewModel.Accept(new ServiceVisitor((typeof(HistoryManager), history.History), (typeof(IPreviewPlayer), player.Object)));
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GradientStopsSlider slider = editor.GetVisualDescendants().OfType<GradientStopsSlider>().Single();
            Thumb thumb = slider.GetVisualDescendants().OfType<Thumb>()
                .OrderBy(t => t.TranslatePoint(default, window)!.Value.X).First();
            IPointer? pointer = null;
            thumb.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            float reached = red.Offset.CurrentValue;
            Assert.That(reached, Is.GreaterThan(0f));
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

            HeadlessTestHelpers.Render(3);
            Assert.That(history.History.IsEditing, Is.False, "The end closes the session before any release.");
            // The slider no longer drags the stop, so a further move or release changes nothing.
            window.MouseMove(start + new Vector(120, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(120, 0), MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
            Assert.Multiple(() =>
            {
                Assert.That(red.Offset.CurrentValue, Is.EqualTo(kept ? reached : 0f));
                Assert.That(history.History.UndoCount, Is.EqualTo(kept ? 1 : 0));
                Assert.That(history.History.IsEditing, Is.False);
            });
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_stop_drag_in_the_brush_flyout_keeps_on_capture_loss_and_reverts_on_Escape(bool escape)
    {
        var brush = new LinearGradientBrush();
        var red = new GradientStop(Colors.Red, 0f);
        brush.GradientStops.Add(red);
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1f));
        var shape = new RectShape { Fill = { CurrentValue = brush } };
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
            GradientStopsSlider slider = window.GetVisualDescendants().OfType<GradientStopsSlider>().Single();
            Thumb thumb = slider.GetVisualDescendants().OfType<Thumb>()
                .OrderBy(t => t.TranslatePoint(default, window)!.Value.X).First();
            IPointer? pointer = null;
            thumb.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            float reached = red.Offset.CurrentValue;
            Assert.That(history.History.IsEditing, Is.True, "The drag must hold an edit session.");

            if (escape)
            {
                window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
            }
            else
            {
                pointer!.Capture(null);
            }

            HeadlessTestHelpers.Render(3);
            Assert.That(history.History.IsEditing, Is.False, "The end closes the session before any release.");
            window.MouseMove(start + new Vector(120, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(120, 0), MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(red.Offset.CurrentValue, Is.EqualTo(escape ? 0f : reached));
                Assert.That(history.History.UndoCount, Is.EqualTo(escape ? 0 : 1));
                Assert.That(history.History.IsEditing, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Typing_a_stop_color_in_the_brush_flyout_opens_no_session()
    {
        var brush = new LinearGradientBrush();
        var red = new GradientStop(Colors.Red, 0f);
        brush.GradientStops.Add(red);
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1f));
        var shape = new RectShape { Fill = { CurrentValue = brush } };
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
            SimpleColorPicker picker = window.GetVisualDescendants().OfType<SimpleColorPicker>().Single();
            TextBox hex = picker.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "HexBox");
            TextBox opacity = picker.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OpacityBox");

            Assert.That(hex.Focus(), Is.True);
            hex.Text = "#00FF00";
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(red.Color.CurrentValue, Is.EqualTo(Color.FromArgb(255, 0, 255, 0)));
                Assert.That(history.History.IsEditing, Is.False, "Typed values must not hold an edit session.");
            });

            // Leaving the box confirms the typed color as one entry.
            Assert.That(opacity.Focus(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(red.Color.CurrentValue, Is.EqualTo(Color.FromArgb(255, 0, 255, 0)));
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
                Assert.That(history.History.IsEditing, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }
}

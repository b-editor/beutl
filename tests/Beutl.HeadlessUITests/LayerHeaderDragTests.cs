using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class LayerHeaderDragTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public async Task Capture_loss_and_playback_move_the_layer_and_Escape_and_Undo_put_it_back(DragEnd end)
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
            $"layer-header-drag-{end}",
            [new ElementDescription(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => new RectShape()))]);
        Element clip = editor.Scene.Children.Single();
        editor.HistoryManager.ExecuteInTransaction(() => clip.Name = "earlier", "Earlier");
        int undoCount = editor.HistoryManager.UndoCount;
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 960, Height = 420 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            LayerHeader header = view.GetVisualDescendants().OfType<LayerHeader>()
                .First(h => h.DataContext is LayerHeaderViewModel { Number: 0 });
            var headerModel = (LayerHeaderViewModel)header.DataContext!;
            Border handle = header.FindControl<Border>("border")!;
            IPointer? pointer = null;
            handle.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point press = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            Point moved = press + new Vector(0, 2 * FrameNumberHelper.LayerHeight);
            TimelineTestHost.WaitForHitTest(window, handle, press);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.HistoryManager.IsEditing, Is.True, "The drag must hold an edit session.");

            switch (end)
            {
                case DragEnd.CaptureLoss:
                    pointer!.Capture(null);
                    break;
                case DragEnd.Playback:
                    editor.Player.IsPlaying.Value = true;
                    HeadlessTestHelpers.Settle();
                    editor.Player.IsPlaying.Value = false;
                    break;
                case DragEnd.Escape:
                    window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    break;
                case DragEnd.Undo:
                    Assert.That(await editor.UndoAsync(), Is.True);
                    break;
            }

            HeadlessTestHelpers.Settle(4);
            Assert.That(editor.HistoryManager.IsEditing, Is.False, "The end closes the session before any release.");
            // The pointer no longer holds the header, so a further move or release changes nothing.
            Point further = moved + new Vector(0, FrameNumberHelper.LayerHeight);
            window.MouseMove(further, RawInputModifiers.LeftMouseButton);
            window.MouseUp(further, MouseButton.Left);
            HeadlessTestHelpers.Settle(4);

            bool moves = end is DragEnd.CaptureLoss or DragEnd.Playback;
            Assert.Multiple(() =>
            {
                Assert.That(clip.ZIndex, Is.EqualTo(moves ? 2 : 0));
                Assert.That(clip.Name, Is.EqualTo("earlier"), "Undo during the drag leaves earlier history alone.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + (moves ? 1 : 0)));
                Assert.That(editor.HistoryManager.IsEditing, Is.False);
                Assert.That(headerModel.PosY.Value, Is.Zero);
            });
            if (!moves)
            {
                ElementViewModel clipModel = timeline.GetViewModelFor(clip)!;
                Assert.That(clipModel.Margin.Value.Top, Is.EqualTo(timeline.CalculateLayerTop(0)).Within(0.0001));
            }
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }
    }
}

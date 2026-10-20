using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class InlineKeyFrameDragTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss, false)]
    [TestCase(DragEnd.Playback, false)]
    [TestCase(DragEnd.Escape, false)]
    [TestCase(DragEnd.Undo, false)]
    [TestCase(DragEnd.CaptureLoss, true)]
    [TestCase(DragEnd.Escape, true)]
    public async Task Capture_loss_and_playback_keep_an_inline_keyframe_drag_and_Escape_and_Undo_revert_it(
        DragEnd end, bool shift)
    {
        using GraphScope graph = await GraphScope.CreateAsync();
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        var property = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        timeline.AttachInline(property, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(item => ReferenceEquals(item.Property, property));
        editor.HistoryManager.ExecuteInTransaction(() => element.Name = "earlier", "Earlier");
        int undoCount = editor.HistoryManager.UndoCount;
        var view = new TimelineTabView { DataContext = timeline };
        graph.Window.Content = view;
        graph.Window.Width = 960;
        try
        {
            HeadlessTestHelpers.Render(3);
            InlineAnimationLayer layer = view.GetVisualDescendants().OfType<InlineAnimationLayer>()
                .Single(item => ReferenceEquals(item.DataContext, inline));
            AvaloniaPath key = layer.GetVisualDescendants().OfType<AvaloniaPath>()
                .Single(item => item.DataContext is InlineKeyFrameViewModel { Model: var model } && model == graph.First);
            IPointer? pointer = null;
            key.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point press = key.TranslatePoint(new Point(key.Bounds.Width / 2, key.Bounds.Height / 2), graph.Window)!.Value;
            Assert.That(graph.HitTest(press), Is.SameAs(key));
            // 150 px per second: move the first key from 0.5 s to 0.9 s.
            Point moved = press + new Vector(60, 0);
            RawInputModifiers modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            graph.Window.MouseMove(press, modifiers);
            graph.Window.MouseDown(press, MouseButton.Left, modifiers);
            graph.Window.MouseMove(moved, modifiers | RawInputModifiers.LeftMouseButton);
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
                    graph.Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    graph.Window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    break;
                case DragEnd.Undo:
                    Assert.That(await editor.UndoAsync(), Is.True);
                    break;
            }

            HeadlessTestHelpers.Settle(4);
            Assert.That(editor.HistoryManager.IsEditing, Is.False, "The end closes the session before any release.");
            // The pointer no longer holds the key, so a further move or release changes nothing.
            graph.Window.MouseMove(moved + new Vector(45, 0), RawInputModifiers.LeftMouseButton);
            graph.Window.MouseUp(moved + new Vector(45, 0), MouseButton.Left);
            HeadlessTestHelpers.Settle(4);

            bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
            var firstModel = (InlineKeyFrameViewModel)key.DataContext!;
            Assert.Multiple(() =>
            {
                Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(kept ? 0.9 : 0.5)));
                Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(kept && shift ? 1.9 : 1.5)));
                Assert.That(firstModel.Left.Value, Is.EqualTo(graph.First.KeyTime.TimeToPixel(1)).Within(0.0001));
                Assert.That(element.Name, Is.EqualTo("earlier"), "Undo during the drag leaves earlier history alone.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + (kept ? 1 : 0)));
                Assert.That(editor.HistoryManager.IsEditing, Is.False);
            });
        }
        finally
        {
            view.DataContext = null;
            timeline.DetachInline(inline);
        }
    }
}

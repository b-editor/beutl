using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementViewDragCancellationTests
{
    [AvaloniaTest]
    [TestCase("left", false, false)]
    [TestCase("right", false, false)]
    [TestCase("move", false, false)]
    [TestCase("duplicate", false, false)]
    [TestCase("left", true, false)]
    [TestCase("right", true, false)]
    [TestCase("move", true, false)]
    [TestCase("duplicate", true, false)]
    [TestCase("right", false, true)]
    [TestCase("move", false, true)]
    public async Task CaptureLost_CancelsPreview_AndAllowsTheNextDragToCommit(string kind, bool grouped, bool snapping)
    {
        bool originalRipple = GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled;
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled;
        GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled = false;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = snapping;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel[] elements) = await OpenElements(grouped);
            TimelineTabViewModel timeline = elements[0].Timeline;
            if (snapping) elements[0].Scene.Duration = TimeSpan.FromSeconds(kind == "right" ? 4.5 : 6);
            view = new TimelineTabView { DataContext = timeline };
            window = new Window { Content = view, Width = 1200, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(5);

            ElementView element = view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, elements[0]));
            Border border = element.FindControl<Border>("border")!;
            var initial = elements.Select(vm => (vm.Model.Start, vm.Model.Length, vm.Model.ZIndex)).ToArray();
            int childCount = view.TimelinePanel.Children.Count;
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            IPointer? pointer = null;
            border.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);

            double x = kind switch
            {
                "left" => 2,
                "right" => border.Bounds.Width - 2,
                _ => border.Bounds.Width / 2
            };
            Point press = border.TranslatePoint(new Point(x, border.Bounds.Height / 2), window)!.Value;
            float scale = timeline.Options.Value.Scale;
            var delta = new Vector(
                TimeSpan.FromSeconds(kind == "left" ? -0.5 : kind == "right" ? 0.5 : 3).TimeToPixel(scale),
                kind is "move" or "duplicate" ? FrameNumberHelper.LayerHeight : 0);
            Point release = press + delta;
            RawInputModifiers modifiers = kind == "duplicate" ? RawInputModifiers.Alt : RawInputModifiers.None;

            DragToPreview();
            Assert.That(pointer, Is.Not.Null, "The actual input event must reach the clip.");
            foreach (ElementViewModel vm in elements)
            {
                if (kind is "left" or "right")
                    Assert.That(vm.Width.Value, Is.Not.EqualTo(vm.Model.Length.TimeToPixel(scale)));
                else
                    Assert.That(vm.BorderMargin.Value.Left, Is.Not.EqualTo(vm.Model.Start.TimeToPixel(scale)));
            }
            if (kind == "duplicate")
                Assert.That(view.TimelinePanel.Children.Count, Is.GreaterThan(childCount), "The duplicate preview must include ghosts.");
            if (snapping)
                Assert.That(timeline.SnapBarPosition.Value, Is.Not.Null, "The drag must display a real snap guide before cancellation.");

            // Cover both platform-style release and capture stolen by another control.
            pointer!.Capture(grouped ? view.FindControl<Border>("RulerBar") : null);
            HeadlessTestHelpers.Render(5);
            AssertOriginalState();
            Assert.Multiple(() =>
            {
                Assert.That(view.TimelinePanel.Children.Count, Is.EqualTo(childCount), "Cancelled ghosts must be removed.");
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False, "Cancellation must not commit the preview.");
            });

            pointer.Capture(null);
            window.MouseUp(new Point(5, 5), MouseButton.Left);
            Point hover = border.TranslatePoint(new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), window)!.Value;
            window.MouseMove(hover);
            HeadlessTestHelpers.Render(5);
            AssertOriginalState();
            Assert.That(view.TimelinePanel.Children.Count, Is.EqualTo(childCount), "Hovering must not resume duplication.");

            // A fresh drag must still commit even though release itself also loses capture.
            DragToPreview();
            window.MouseUp(release, MouseButton.Left, modifiers);
            HeadlessTestHelpers.Settle(4);
            for (int i = 0; i < elements.Length; i++)
            {
                Element model = elements[i].Model;
                Assert.Multiple(() =>
                {
                    Assert.That(model.Start, Is.EqualTo(initial[i].Start + TimeSpan.FromSeconds(kind == "left" ? -0.5 : kind == "move" ? 3 : 0)));
                    Assert.That(model.Length, Is.EqualTo(initial[i].Length + TimeSpan.FromSeconds(kind is "left" or "right" ? 0.5 : 0)));
                    Assert.That(model.ZIndex, Is.EqualTo(initial[i].ZIndex + (kind == "move" ? 1 : 0)));
                });
            }
            Assert.That(elements[0].Scene.Children.Count, Is.EqualTo(elements.Length * (kind == "duplicate" ? 2 : 1)));
            if (kind == "duplicate")
            {
                Element[] copies = elements[0].Scene.Children.Except(elements.Select(vm => vm.Model)).ToArray();
                Assert.That(copies.Select(copy => copy.Start), Is.All.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(copies.Select(copy => copy.ZIndex).Order(), Is.EqualTo(initial.Select(item => item.ZIndex + 1).Order()));
            }
            await Task.Delay(300);
            HeadlessTestHelpers.Render(5);
            foreach (ElementViewModel vm in elements) AssertVisualMatchesModel(vm);

            void DragToPreview()
            {
                window.MouseMove(press, modifiers);
                window.MouseDown(press, MouseButton.Left, modifiers);
                window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(5);
            }

            void AssertOriginalState()
            {
                Assert.That(elements[0].Scene.Children.Count, Is.EqualTo(elements.Length));
                for (int i = 0; i < elements.Length; i++)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(elements[i].Model.Start, Is.EqualTo(initial[i].Start));
                        Assert.That(elements[i].Model.Length, Is.EqualTo(initial[i].Length));
                        Assert.That(elements[i].Model.ZIndex, Is.EqualTo(initial[i].ZIndex));
                    });
                    AssertVisualMatchesModel(elements[i]);
                }
            }
        }
        finally
        {
            TimelineTestHost.Close(view, window);
            GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled = originalRipple;
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = originalSnap;
        }
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control)]
    [TestCase(RawInputModifiers.Shift)]
    public async Task CaptureLost_ModifierDrag_RestoresTheUnselectedPressedClip(RawInputModifiers modifiers)
    {
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel[] elements) = await OpenElements(grouped: false, count: 2);
            ElementViewModel selected = elements[0];
            ElementViewModel pressed = elements[1];
            TimelineTabViewModel timeline = pressed.Timeline;
            timeline.ClearSelected();
            timeline.SelectElement(selected);
            Assert.That(pressed.GetGroupOrSelectedElements(), Does.Not.Contain(pressed));
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            view = new TimelineTabView { DataContext = timeline };
            window = new Window { Content = view, Width = 1200, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView element = view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, pressed));
            Border border = element.FindControl<Border>("border")!;
            IPointer? pointer = null;
            border.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point press = border.TranslatePoint(new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), window)!.Value;
            Point release = press + new Vector(TimeSpan.FromSeconds(1).TimeToPixel(timeline.Options.Value.Scale), FrameNumberHelper.LayerHeight);
            window.MouseMove(press, modifiers);
            window.MouseDown(press, MouseButton.Left, modifiers);
            window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(5);
            Assert.That(pointer, Is.Not.Null);
            Assert.That(timeline.SelectedElements, Is.EqualTo(new[] { selected }), "Modifier drag must leave the pressed clip unselected.");
            foreach (ElementViewModel vm in elements)
                Assert.That(vm.BorderMargin.Value.Left, Is.Not.EqualTo(vm.Model.Start.TimeToPixel(timeline.Options.Value.Scale)));

            pointer!.Capture(null);
            HeadlessTestHelpers.Render(5);
            foreach (ElementViewModel vm in elements) AssertVisualMatchesModel(vm);
            Assert.Multiple(() =>
            {
                Assert.That(elements.Select(vm => vm.Model.Start), Is.All.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(elements.Select(vm => vm.Model.Length), Is.All.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(elements.Select(vm => vm.Model.ZIndex), Is.EqualTo(new[] { 0, 1 }));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            });
            window.MouseUp(new Point(5, 5), MouseButton.Left);
            Point hover = border.TranslatePoint(new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), window)!.Value;
            window.MouseMove(hover);
            HeadlessTestHelpers.Render(5);
            foreach (ElementViewModel vm in elements) AssertVisualMatchesModel(vm);
        }
        finally
        {
            TimelineTestHost.Close(view, window);
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = originalSnap;
        }
    }

    private static async Task<(EditViewModel Editor, ElementViewModel[] Elements)> OpenElements(bool grouped, int count = 1)
    {
        ElementDescription[] descriptions = Enumerable.Range(0, grouped ? 2 : count)
            .Select(layer => new ElementDescription(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), layer,
                new ElementSource.EngineObject(() => new RectShape())))
            .ToArray();
        (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
            $"element-drag-cancel-{Guid.NewGuid():N}", descriptions);
        ElementViewModel[] elements = editor.Scene.Children.OrderBy(model => model.ZIndex)
            .Select(model => timeline.GetViewModelFor(model)!).ToArray();
        timeline.ClearSelected();
        foreach (ElementViewModel vm in elements) timeline.SelectElement(vm);
        if (grouped) elements[0].GroupSelectedElements.Execute();
        HeadlessTestHelpers.Settle();
        return (editor, elements);
    }

    private static void AssertVisualMatchesModel(ElementViewModel vm)
    {
        float scale = vm.Timeline.Options.Value.Scale;
        Assert.Multiple(() =>
        {
            Assert.That(vm.BorderMargin.Value.Left, Is.EqualTo(vm.Model.Start.TimeToPixel(scale)).Within(0.0001));
            Assert.That(vm.Width.Value, Is.EqualTo(vm.Model.Length.TimeToPixel(scale)).Within(0.0001));
            Assert.That(vm.Margin.Value.Top, Is.EqualTo(vm.Timeline.CalculateLayerTop(vm.Model.ZIndex)).Within(0.0001));
        });
    }
}

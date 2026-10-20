using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.Curves;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.Views;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using AvaPoint = Avalonia.Point;
using BtlPoint = Beutl.Graphics.Point;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CurveEditorDragTests
{
    // Sorting moves the dragged point past its neighbor; the drag must stay on that point.
    [AvaloniaTest]
    public void Dragging_a_point_past_its_neighbor_keeps_dragging_that_point()
    {
        var points = new List<CurveControlPoint> { new(0, 0), new(0.3f, 0.3f), new(0.6f, 0.6f), new(1, 1) };
        var editor = new CurveEditor { Points = points };
        var window = new Window { Content = editor, Width = 200, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            AvaPoint At(float x, float y)
                => editor.TranslatePoint(new AvaPoint(x * editor.Bounds.Width, (1 - y) * editor.Bounds.Height), window)!.Value;

            window.MouseDown(At(0.3f, 0.3f), MouseButton.Left);
            window.MouseMove(At(0.8f, 0.3f));
            window.MouseMove(At(0.9f, 0.5f));
            window.MouseUp(At(0.9f, 0.5f), MouseButton.Left);

            Assert.That(points.Select(p => p.Point), Is.EqualTo(new[]
            {
                new BtlPoint(0, 0), new BtlPoint(0.6f, 0.6f), new BtlPoint(0.9f, 0.5f), new BtlPoint(1, 1)
            }));
        }
        finally
        {
            window.Close();
        }
    }

    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public async Task Capture_loss_and_playback_keep_a_curve_drag_and_Escape_and_Undo_revert_it(DragEnd end)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"curve-drag-{end}");
        var curves = new Curves();
        var shape = new RectShape();
        shape.FilterEffect.CurrentValue = curves;
        await TestShell.AddElementAsync(editor, () => shape, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element element = scene.Children.Single();
        editor.HistoryManager.ExecuteInTransaction(() => element.Name = "earlier", "Earlier");
        int undoCount = editor.HistoryManager.UndoCount;
        using var model = new CurvesTabViewModel(editor);
        model.Effect.Value = curves;
        var view = new CurvesTabView { DataContext = model };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            CurveEditor curve = view.GetVisualDescendants().OfType<CurveEditor>().Single(c => c.IsEffectivelyVisible);
            IPointer? pointer = null;
            curve.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            AvaPoint At(double x, double y)
                => curve.TranslatePoint(new AvaPoint(x * curve.Bounds.Width, (1 - y) * curve.Bounds.Height), window)!.Value;

            // A press off the curve adds a point there, and the drag moves it.
            window.MouseMove(At(0.5, 0.25));
            window.MouseDown(At(0.5, 0.25), MouseButton.Left);
            window.MouseMove(At(0.6, 0.5), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            Assert.That(curves.MasterCurve.CurrentValue.Points, Has.Length.EqualTo(3));
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
            // The editor no longer drags the point, so a further move or release changes nothing.
            window.MouseMove(At(0.7, 0.8), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(0.7, 0.8), MouseButton.Left);
            HeadlessTestHelpers.Settle(4);

            bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
            System.Collections.Immutable.ImmutableArray<CurveControlPoint> points = curves.MasterCurve.CurrentValue.Points;
            Assert.Multiple(() =>
            {
                Assert.That(points, Has.Length.EqualTo(kept ? 3 : 2));
                if (kept)
                {
                    Assert.That(points[1].Point.X, Is.EqualTo(0.6f).Within(0.01f));
                    Assert.That(points[1].Point.Y, Is.EqualTo(0.5f).Within(0.01f));
                }
                Assert.That(model.MasterCurve.Value!.Points, Has.Count.EqualTo(points.Length));
                Assert.That(element.Name, Is.EqualTo("earlier"), "Undo during the drag leaves earlier history alone.");
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + (kept ? 1 : 0)));
                Assert.That(editor.HistoryManager.IsEditing, Is.False);
            });
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }
}

using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Moq;
using Reactive.Bindings;
using SkiaSharp;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PathEditorInteractionTests
{
    [AvaloniaTest]
    public async Task Click_shift_marquee_and_nudge_share_one_selection_and_history()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[0] }));
        editor.Click(180, 100, RawInputModifiers.Shift);
        Assert.That(editor.Selected, Has.Length.EqualTo(2));
        editor.Click(80, 100, RawInputModifiers.Shift);
        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[1] }));
        editor.Drag(new(60, 75), new(205, 125));
        Assert.That(editor.Selected, Has.Length.EqualTo(2));
        editor.Key(Key.Right, RawInputModifiers.Shift);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(90, 100)));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Value, Is.EqualTo(new BtlPoint(190, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().Value, Is.EqualTo(new BtlPoint(280, 180)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        Assert.That(editor.Editor.HistoryManager.Undo(), Is.True);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
        editor.Key(Key.Escape);
        editor.Key(Key.Right);
        Assert.That(editor.Selected, Is.Empty);
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Value, Is.EqualTo(new BtlPoint(180, 100)));
        editor.Drag(new(60, 75), new(110, 125));
        editor.Drag(new(260, 160), new(300, 200), RawInputModifiers.Shift);
        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[0], editor.Figure.Segments[2] }));
    }

    [AvaloniaTest]
    public async Task Pressing_a_point_leaves_the_segment_editors_as_they_were()
    {
        using var editor = await Fixture.Create();
        Element element = editor.Editor.Scene.Children.Single();
        element.Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"path-edit-{Guid.NewGuid():N}.belm"));
        ((IEditorSelection)editor.Editor.GetService(typeof(IEditorSelection))!).Select(element);
        GeometryEditorViewModel geometry = editor.Editor.FindToolTab<ElementPropertyTabViewModel>()!.Items
            .SelectMany(item => item.Properties)
            .OfType<GeometryEditorViewModel>()
            .Single();
        geometry.ExpandForEditing();
        var figure = (PathFigureEditorViewModel)geometry.FindPathFigureContext(editor.Figure)!;
        editor.Model.StartOrFinishEdit(figure);
        HeadlessTestHelpers.Render(3);

        editor.Click(180, 100);

        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[1] }));
        Assert.That(figure.Group.Value!.Items.Select(item => ((PathSegmentEditorViewModel)item.Context!).IsExpanded.Value),
            Is.All.False);
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control, false, false)]
    [TestCase(RawInputModifiers.Meta, false, false)]
    [TestCase(RawInputModifiers.Control, true, false)]
    [TestCase(RawInputModifiers.Meta, true, false)]
    [TestCase(RawInputModifiers.Control, false, true)]
    [TestCase(RawInputModifiers.Meta, false, true)]
    [TestCase(RawInputModifiers.Control, true, true)]
    [TestCase(RawInputModifiers.Meta, true, true)]
    public async Task Control_and_command_click_toggle_points_in_the_selection(
        RawInputModifiers modifier, bool preview, bool moveOnHover)
    {
        using var editor = await Fixture.Create();
        PathEditorView? overlay = preview ? editor.UsePreviewOverlay() : null;
        IPathEditorView view = overlay is null ? editor.View : overlay;
        ClickAnchor(0);
        Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[0] }));
        Thumb anchor = FindAnchor(1);
        Point positionBeforeHover = default;
        bool moved = false;
        if (moveOnHover)
        {
            // Reproduce layout work invalidating coordinates between mouse move and press.
            editor.Window.PointerMoved += (_, _) =>
            {
                if (moved) return;
                moved = true;
                positionBeforeHover = anchor.TranslatePoint(default, editor.Window)!.Value;
                if (overlay != null) overlay.Matrix *= Matrix.CreateTranslation(40, 30);
                else editor.View.Matrix *= Matrix.CreateTranslation(40, 30);
            };
        }
        editor.ClickControl(anchor, modifier, beforePress: () =>
        {
            Assert.That(moved, Is.EqualTo(moveOnHover));
            if (moveOnHover)
                Assert.That(anchor.TranslatePoint(default, editor.Window)!.Value,
                    Is.Not.EqualTo(positionBeforeHover), "The anchor must move in window coordinates before the mouse press.");
        });
        Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[0], editor.Figure.Segments[1] }));
        ClickAnchor(0, modifier);
        Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[1] }));
        ClickAnchor(2);
        Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[2] }));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);

        PathSegment[] Selected() => view.GetSelectedAnchors().Select(t => (PathSegment)t.DataContext!).ToArray();

        Thumb FindAnchor(int index) => Fixture.FindAnchor(view, editor.Figure.Segments[index]);

        void ClickAnchor(int index, RawInputModifiers modifiers = RawInputModifiers.None)
            => editor.ClickControl(FindAnchor(index), modifiers);
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Pen_does_not_sample_a_driven_start_into_new_static_controls(bool empty, bool expression)
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        if (!empty) figure.Segments.Add(new LineSegment(180, 100));
        using var editor = await Fixture.Create(pathFigure: figure);
        var source = empty ? figure.StartPoint : figure.Segments[^1].GetEndPoint();
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (expression) source.Expression = Expression.Create<BtlPoint>(empty ? "new Point(80, 100)" : "new Point(180, 100)");
            else
            {
                var animation = new KeyFrameAnimation<BtlPoint>();
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = { Value = source.Value } });
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = new(100, 180) } });
                source.SetKeyFrameAnimation(animation);
            }
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var before = CoreSerializer.SerializeToJsonObject(figure);
        editor.Key(Key.P);
        editor.Drag(new(240, 160), new(270, 160));
        Assert.That(JsonNode.DeepEquals(before, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Pen));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            source.SetKeyFrameAnimation(null);
            source.Expression = null;
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        editor.Drag(new(240, 160), new(270, 160));
        Assert.That(figure.Segments, Has.Count.EqualTo(empty ? 1 : 2));
        Assert.That(((CubicBezierSegment)figure.Segments[^1]).ControlPoint2.Value, Is.EqualTo(new BtlPoint(210, 160)));
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Shift, false)]
    [TestCase(RawInputModifiers.Control, false)]
    [TestCase(RawInputModifiers.Meta, false)]
    [TestCase(RawInputModifiers.Shift, true)]
    [TestCase(RawInputModifiers.Control, true)]
    [TestCase(RawInputModifiers.Meta, true)]
    public async Task Extending_selection_to_an_earlier_anchor_keeps_it_active(RawInputModifiers modifiers, bool preview)
    {
        using var editor = await Fixture.Create();
        var model = editor.Editor.Player.PathEditor;
        IPathEditorView view = preview ? editor.UsePreviewOverlay() : editor.View;
        ClickAnchor(2);
        ClickAnchor(0, modifiers);
        Assert.That(view.GetSelectedAnchors(), Has.Length.EqualTo(2));
        Assert.That(Active(), Is.SameAs(editor.Figure.Segments[0]));
        if (!preview)
            Assert.That(((PropertyEditorViewModel)editor.Model.PointProperties.Value[0]).PropertyAdapter.GetEngineProperty(),
                Is.SameAs(editor.Figure.Segments[0].GetEndPoint()));
        ClickAnchor(1, modifiers);
        Assert.That(Active(), Is.SameAs(editor.Figure.Segments[1]));
        ClickAnchor(0, modifiers);
        Assert.That(view.GetSelectedAnchors(), Has.Length.EqualTo(2));
        Assert.That(Active(), Is.SameAs(editor.Figure.Segments[2]));
        ClickAnchor(1, modifiers);
        Assert.That(view.GetSelectedAnchors(), Has.Length.EqualTo(1));
        Assert.That(Active(), Is.SameAs(editor.Figure.Segments[2]));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);

        PathSegment? Active() => preview ? model.SelectedOperation.Value : editor.Model.SelectedOperation.Value;
        void ClickAnchor(int index, RawInputModifiers modifier = RawInputModifiers.None)
            => editor.ClickControl(Fixture.FindAnchor(view, editor.Figure.Segments[index]), modifier);
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control, false)]
    [TestCase(RawInputModifiers.Meta, false)]
    [TestCase(RawInputModifiers.Control, true)]
    [TestCase(RawInputModifiers.Meta, true)]
    public async Task Player_path_modifier_wheel_zooms_in_global_move_mode(RawInputModifiers modifier, bool pathHand)
    {
        using var editor = await Fixture.Create();
        var model = editor.Editor.Player;
        model.PathEditor.FigureContext.Value = editor.Model.FigureContext.Value;
        model.IsMoveMode.Value = true;
        model.IsHandMode.Value = false;
        var player = new PlayerView { DataContext = model };
        editor.Window.Content = player;
        HeadlessTestHelpers.Render(5);
        try
        {
            var frame = player.FindControl<Panel>("framePanel")!;
            var path = player.FindControl<PathEditorView>("pathEditorView")!;
            path.Tool = pathHand ? PathEditorTool.Hand : PathEditorTool.Move;
            Point local = new(frame.Bounds.Width / 2, frame.Bounds.Height / 2);
            Point cursor = frame.TranslatePoint(local, editor.Window)!.Value;
            var before = model.FrameMatrix.Value;
            editor.Window.MouseWheel(cursor, new Vector(0, 1), modifier);
            HeadlessTestHelpers.Render(3);
            Assert.That(model.FrameMatrix.Value.M11, Is.GreaterThan(before.M11));
            Assert.That(((Vector)(frame.TranslatePoint(local, editor.Window)!.Value - cursor)).Length, Is.LessThan(.01));
            Assert.That(model.IsMoveMode.Value, Is.True);
            Assert.That(model.IsHandMode.Value, Is.False);
            var zoomed = model.FrameMatrix.Value;
            editor.Window.MouseWheel(cursor, new Vector(0, 1));
            HeadlessTestHelpers.Render(3);
            Assert.That(model.FrameMatrix.Value.M11, Is.EqualTo(zoomed.M11));
            Assert.That(model.FrameMatrix.Value.M32, Is.EqualTo(zoomed.M32 + 24).Within(.01));
        }
        finally { player.DataContext = null; model.PathEditor.FigureContext.Value = null; }
    }

    [AvaloniaTest]
    public async Task Drag_selected_points_constrains_axis_and_undo_restores_the_group()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        editor.Click(180, 100, RawInputModifiers.Shift);
        editor.Drag(new(80, 100), new(120, 117), RawInputModifiers.Shift);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(120, 100)));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Value, Is.EqualTo(new BtlPoint(220, 100)));
        Assert.That(editor.Selected, Has.Length.EqualTo(2));
        Assert.That(editor.View.SkipUpdatePosition, Is.False);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
    }

    [AvaloniaTest]
    public async Task Pan_and_zoom_do_not_move_points_and_zoom_is_centered_on_pointer()
    {
        using var editor = await Fixture.Create();
        editor.View.Focus();
        editor.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        editor.Drag(new(80, 100), new(115, 120));
        editor.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.That(editor.View.Matrix.M31, Is.EqualTo(35));
        Assert.That(editor.View.Matrix.M32, Is.EqualTo(20));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
        Point screen = new(230, 170);
        Point local = editor.View.Matrix.Invert().Transform(screen);
        editor.MouseWheel(screen, new Vector(0, 1), RawInputModifiers.Control);
        Assert.That(((Vector)(editor.View.Matrix.Transform(local) - screen)).Length, Is.LessThan(.001));
        Assert.That(editor.View.Matrix.M11, Is.GreaterThan(1));
        editor.Key(Key.D0);
        Assert.That(editor.View.Matrix, Is.EqualTo(Matrix.Identity));
        editor.Drag(new(300, 260), new(330, 280), button: MouseButton.Middle);
        Assert.That(editor.View.Matrix.M31, Is.EqualTo(30));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public async Task Pen_adds_curves_closes_path_and_delete_is_one_undo_step()
    {
        using var editor = await Fixture.Create(empty: true);
        editor.Key(Key.P);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Pen));
        editor.Click(80, 100);
        editor.Drag(new(180, 160), new(215, 160));
        editor.Click(290, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
        var curve = (CubicBezierSegment)editor.Figure.Segments[1];
        Assert.That(curve.ControlPoint2.Value, Is.EqualTo(new BtlPoint(145, 160)));
        Assert.That(((CubicBezierSegment)editor.Figure.Segments[2]).ControlPoint1.Value, Is.EqualTo(new BtlPoint(215, 160)));
        editor.Click(80, 100);
        Assert.That(editor.Figure.IsClosed.Value, Is.True);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Move));
        editor.Click(180, 160);
        editor.Click(290, 100, RawInputModifiers.Shift);
        int before = editor.Editor.HistoryManager.UndoCount;
        editor.Key(Key.Delete);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(1));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Pen_creates_the_first_explicit_edge_with_drag_handles_and_one_history_entry(bool drag)
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        if (drag) editor.Drag(new(180, 160), new(215, 160));
        else editor.Click(180, 160);
        Assert.That(figure.Segments, Has.Count.EqualTo(1));
        var first = figure.Segments[0];
        Assert.That(first.GetEndPoint().Value, Is.EqualTo(new BtlPoint(180, 160)));
        Assert.That(figure.StartPoint.Value, Is.EqualTo(new BtlPoint(80, 100)));
        var midpoint = PathEditingOperations.Evaluate(figure.StartPoint.Value, first, .5f, CompositionContext.Default);
        if (drag)
        {
            Assert.That(first, Is.TypeOf<CubicBezierSegment>());
            Assert.That(((CubicBezierSegment)first).ControlPoint2.Value, Is.EqualTo(new BtlPoint(145, 160)));
            Assert.That(midpoint.Y, Is.Not.EqualTo(130));
        }
        else Assert.That(midpoint.Y, Is.EqualTo(130).Within(.001));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        var firstState = CoreSerializer.SerializeToJsonObject(figure);
        var firstEdge = CoreSerializer.SerializeToJsonObject(first);
        editor.Click(290, 100);
        Assert.That(figure.Segments, Has.Count.EqualTo(2));
        if (drag) Assert.That(((CubicBezierSegment)figure.Segments[1]).ControlPoint1.Value, Is.EqualTo(new BtlPoint(215, 160)));
        var secondState = CoreSerializer.SerializeToJsonObject(figure);
        editor.Click(80, 100);
        Assert.That(figure.IsClosed.Value, Is.True);
        Assert.That(JsonNode.DeepEquals(firstEdge, CoreSerializer.SerializeToJsonObject(first)), Is.True);
        var closedState = CoreSerializer.SerializeToJsonObject(figure);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(3));
        editor.Editor.HistoryManager.Undo();
        Assert.That(JsonNode.DeepEquals(secondState, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        editor.Editor.HistoryManager.Undo();
        Assert.That(JsonNode.DeepEquals(firstState, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        editor.Editor.HistoryManager.Undo();
        Assert.That(figure.Segments, Is.Empty);
        Assert.That(figure.StartPoint.Value, Is.EqualTo(new BtlPoint(80, 100)));
        editor.Editor.HistoryManager.Redo();
        editor.Editor.HistoryManager.Redo();
        editor.Editor.HistoryManager.Redo();
        Assert.That(JsonNode.DeepEquals(closedState, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
    }

    [AvaloniaTest]
    public async Task Escape_cancels_first_explicit_edge_drag_without_losing_the_start_point()
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.MouseDown(new(180, 160), MouseButton.Left);
        editor.MouseMove(new(215, 160), RawInputModifiers.LeftMouseButton);
        Assert.That(figure.Segments.Single(), Is.TypeOf<CubicBezierSegment>());
        editor.Key(Key.Escape);
        editor.MouseUp(new(215, 160), MouseButton.Left);
        Assert.That(figure.Segments, Is.Empty);
        Assert.That(figure.StartPoint.Value, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Pen_closing_existing_cubic_applies_outgoing_and_preserves_incoming(bool animatedIncoming)
    {
        var figure = new PathFigure();
        var first = PathEditingOperations.Cubic(new(30, 90), new(40, 140), new(80, 100));
        if (animatedIncoming)
        {
            var animation = new KeyFrameAnimation<BtlPoint>();
            animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = { Value = new(35, 85) } });
            first.ControlPoint2.SetKeyFrameAnimation(animation);
        }
        figure.Segments.Add(first);
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.Click(280, 180);
        editor.Drag(new(240, 120), new(270, 120));
        var before = CoreSerializer.SerializeToJsonObject(figure);
        var incoming = first.ControlPoint2.GetKeyFrameAnimation();
        int history = editor.Editor.HistoryManager.UndoCount;
        editor.Click(80, 100);
        Assert.That(figure.IsClosed.Value, Is.True);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Move));
        Assert.That(figure.Segments[0], Is.SameAs(first));
        Assert.That(first.ControlPoint1.Value, Is.EqualTo(new BtlPoint(270, 120)));
        Assert.That(first.ControlPoint2.Value, Is.EqualTo(new BtlPoint(40, 140)));
        Assert.That(first.ControlPoint2.GetKeyFrameAnimation(), Is.SameAs(incoming));
        Assert.That(first.EndPoint.Value, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
        var after = CoreSerializer.SerializeToJsonObject(figure);
        editor.Editor.HistoryManager.Undo();
        Assert.That(JsonNode.DeepEquals(before, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        editor.Editor.HistoryManager.Redo();
        Assert.That(JsonNode.DeepEquals(after, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
    }

    [AvaloniaTest]
    [TestCase("animation")]
    [TestCase("expression")]
    [TestCase("quadratic")]
    [TestCase("last-point-animation")]
    public async Task Pen_rejects_incompatible_implicit_closing_edge_without_losing_outgoing(string kind)
    {
        var figure = new PathFigure();
        var cubic = PathEditingOperations.Cubic(new(30, 90), new(40, 140), new(80, 100));
        figure.Segments.Add(kind == "quadratic" ? new QuadraticBezierSegment(new(40, 140), new(80, 100)) : cubic);
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.Click(280, 180);
        editor.Drag(new(240, 120), new(270, 120));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (kind == "expression") cubic.ControlPoint1.Expression = Expression.Create<BtlPoint>("new Point(30, 90)");
            if (kind is "animation" or "last-point-animation")
            {
                var property = kind == "animation" ? cubic.ControlPoint1 : figure.Segments[^1].GetEndPoint();
                var animation = new KeyFrameAnimation<BtlPoint>();
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = { Value = property.Value } });
                property.SetKeyFrameAnimation(animation);
            }
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var before = CoreSerializer.SerializeToJsonObject(figure);
        int history = editor.Editor.HistoryManager.UndoCount;
        editor.Click(80, 100);
        Assert.That(JsonNode.DeepEquals(before, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Pen));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history));
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
        if (kind == "last-point-animation")
        {
            // Appending from a driven endpoint is protected just like closing it.
            using (editor.Editor.HistoryManager.SuppressRecording())
                figure.Segments[^1].GetEndPoint().SetKeyFrameAnimation(null);
            editor.View.Refresh();
            HeadlessTestHelpers.Render(3);
        }
        editor.Click(220, 160);
        Assert.That(((CubicBezierSegment)figure.Segments[^1]).ControlPoint1.Value, Is.EqualTo(new BtlPoint(270, 120)));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Pen_closes_at_explicit_start_without_changing_the_first_edge(bool curved, bool preview)
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        var first = new LineSegment(180, 100);
        figure.Segments.Add(first);
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        IPathEditorView view = editor.View;
        if (preview)
        {
            PathEditorView overlay = editor.UsePreviewOverlay();
            overlay.Tool = PathEditorTool.Pen;
            view = overlay;
        }
        else editor.Key(Key.P);
        // Preview matrices are published asynchronously; hit the displayed
        // anchor instead of predicting where its next layout will place it.
        ClickAnchor(figure.Segments[^1]);
        Assert.That(figure.Segments, Has.Count.EqualTo(2));
        Assert.That(view.GetSelectedAnchors().Single().DataContext, Is.SameAs(figure.Segments[^1]));
        if (curved) editor.Drag(Screen(new(240, 120)), Screen(new(270, 120)));
        else Click(new(240, 120));
        Assert.That(figure.Segments, Has.Count.EqualTo(3), "append the endpoint before testing closure");
        var originalEdges = figure.Segments.ToArray();
        var originalFirst = CoreSerializer.SerializeToJsonObject(first);
        int history = editor.Editor.HistoryManager.UndoCount;
        ClickAnchor(first);
        Assert.That(figure.IsClosed.Value, Is.False, "the first segment endpoint is not the start");
        Assert.That(figure.Segments, Is.EqualTo(originalEdges));
        Assert.That(JsonNode.DeepEquals(originalFirst, CoreSerializer.SerializeToJsonObject(first)), Is.True);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history));

        var startMarker = ((Control)view).FindControl<Canvas>("canvas")!.Children.OfType<Border>()
            .Single(b => b.Name == "PathStartPoint");
        editor.ClickControl(startMarker);
        Assert.That(figure.IsClosed.Value, Is.True);
        Assert.That(figure.Segments.Take(originalEdges.Length), Is.EqualTo(originalEdges));
        Assert.That(JsonNode.DeepEquals(originalFirst, CoreSerializer.SerializeToJsonObject(first)), Is.True);
        Assert.That(figure.Segments, Has.Count.EqualTo(originalEdges.Length + (curved ? 1 : 0)));
        if (curved)
        {
            var closing = (CubicBezierSegment)figure.Segments[^1];
            Assert.That(closing.EndPoint.Value, Is.EqualTo(figure.StartPoint.Value));
            Assert.That(closing.ControlPoint1.Value, Is.EqualTo(new BtlPoint(270, 120)));
        }
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(figure.IsClosed.Value, Is.False);
        Assert.That(figure.Segments, Is.EqualTo(originalEdges));

        Point Screen(BtlPoint p) => view.Matrix.Transform(new Point(p.X, p.Y)) * view.Scale;
        void Click(BtlPoint p) { Point screen = Screen(p); editor.Click(screen.X, screen.Y); }
        void ClickAnchor(PathSegment segment) => editor.ClickControl(Fixture.FindAnchor(view, segment));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Pen_rejects_closure_when_closed_state_has_animation_or_expression(bool explicitStart, bool expression)
    {
        var figure = new PathFigure();
        if (explicitStart) figure.StartPoint.Value = new(80, 100);
        else figure.Segments.Add(new LineSegment(80, 100));
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.Click(280, 180);
        editor.Drag(new(240, 120), new(270, 120));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (expression)
            {
                figure.IsClosed.Expression = Expression.Create<bool>("false");
            }
            else
            {
                var animation = new KeyFrameAnimation<bool>();
                animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = false } });
                animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = true } });
                figure.IsClosed.SetKeyFrameAnimation(animation);
            }
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var before = CoreSerializer.SerializeToJsonObject(figure);
        int history = editor.Editor.HistoryManager.UndoCount;
        editor.Click(80, 100);
        Assert.That(JsonNode.DeepEquals(before, CoreSerializer.SerializeToJsonObject(figure)), Is.True,
            "reject the entire close operation without touching geometry, keys, expressions, or base values");
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Pen));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history));
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
        editor.Click(220, 160);
        Assert.That(((CubicBezierSegment)figure.Segments[^1]).ControlPoint1.Value, Is.EqualTo(new BtlPoint(270, 120)),
            "a rejected closure must preserve the pending outgoing handle for continuation");
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Pen_retains_pending_handle_when_closing_endpoint_is_not_static(bool last, bool expression)
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.Click(280, 180);
        editor.Drag(new(240, 120), new(270, 120));
        var property = last ? figure.Segments[^1].GetEndPoint() : figure.StartPoint;
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (expression)
            {
                property.Expression = Expression.Create<BtlPoint>(last ? "new Point(240, 120)" : "new Point(80, 100)");
            }
            else
            {
                var animation = new KeyFrameAnimation<BtlPoint>();
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = { Value = property.Value } });
                property.SetKeyFrameAnimation(animation);
            }
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var before = CoreSerializer.SerializeToJsonObject(figure);
        int history = editor.Editor.HistoryManager.UndoCount;
        editor.Click(80, 100);
        Assert.That(JsonNode.DeepEquals(before, CoreSerializer.SerializeToJsonObject(figure)), Is.True);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Pen));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history));
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            property.SetKeyFrameAnimation(null);
            property.Expression = null;
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        editor.Click(80, 100);
        Assert.That(figure.IsClosed.Value, Is.True);
        var closing = (CubicBezierSegment)figure.Segments[^1];
        Assert.That(closing.EndPoint.Value, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(closing.ControlPoint1.Value, Is.EqualTo(new BtlPoint(270, 120)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
    }

    [AvaloniaTest]
    public async Task Explicit_start_marker_requires_a_live_pen_continuation()
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        var marker = editor.View.FindControl<Canvas>("canvas")!.Children.OfType<Border>()
            .Single(b => b.Name == "PathStartPoint");
        editor.Key(Key.P);
        Assert.That(marker.IsVisible, Is.False, "selecting Pen alone cannot offer closure");
        editor.Click(280, 180);
        Assert.That(marker.IsVisible, Is.True);
        editor.Key(Key.V);
        Assert.That(marker.IsVisible, Is.False);
        editor.Key(Key.P);
        Assert.That(marker.IsVisible, Is.False);
        editor.Click(180, 100);
        Assert.That(marker.IsVisible, Is.False);
        editor.Click(280, 180);
        Assert.That(marker.IsVisible, Is.True);
        editor.Key(Key.Delete);
        Assert.That(marker.IsVisible, Is.False, "deleting the continuation endpoint clears the close target");
        editor.MouseMove(new(350, 300));
        Assert.That(marker.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public async Task Player_path_toolbar_stays_in_the_viewport_when_the_frame_is_transformed()
    {
        using var editor = await Fixture.Create();
        var model = editor.Editor.Player;
        model.PathEditor.FigureContext.Value = editor.Model.FigureContext.Value;
        var player = new PlayerView { DataContext = model };
        editor.Window.Content = player;
        HeadlessTestHelpers.Render(5);
        try
        {
            var bar = player.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Child is StackPanel { Name: "PathTools" });
            var tools = (StackPanel)bar.Child!;
            var pathView = player.FindControl<PathEditorView>("pathEditorView")!;
            Point before = bar.TranslatePoint(default, editor.Window)!.Value;
            Point beforeEnd = bar.TranslatePoint(new Point(bar.Bounds.Width, bar.Bounds.Height), editor.Window)!.Value;
            model.FrameMatrix.Value = Beutl.Graphics.Matrix.CreateScale(2, 2) * Beutl.Graphics.Matrix.CreateTranslation(150, -80);
            HeadlessTestHelpers.Render(5);
            Assert.That(bar.TranslatePoint(default, editor.Window), Is.EqualTo(before));
            Assert.That(bar.TranslatePoint(new Point(bar.Bounds.Width, bar.Bounds.Height), editor.Window), Is.EqualTo(beforeEnd));
            editor.ClickControl(tools.Children.OfType<RadioButton>().ElementAt(1));
            Assert.That(pathView.Tool, Is.EqualTo(PathEditorTool.Pen));
            editor.Window.Width = 820;
            HeadlessTestHelpers.Render(3);
            var position = bar.TranslatePoint(default, editor.Window)!.Value;
            Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(position.X + bar.Bounds.Width, Is.LessThanOrEqualTo(editor.Window.Bounds.Width));
            model.PathEditor.FigureContext.Value = null;
            HeadlessTestHelpers.Render(3);
            Assert.That(bar.IsEffectivelyVisible, Is.False);
        }
        finally { player.DataContext = null; model.PathEditor.FigureContext.Value = null; }
    }

    [AvaloniaTest]
    public async Task Hover_culls_distant_beziers_before_subdivision_at_high_zoom()
    {
        using var editor = await Fixture.Create(empty: true);
        int evaluations = 0;
        const int count = 12;
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.StartPoint.Value = new(10000, 10000);
            for (int i = 0; i < count; i++)
            {
                float x = 10000 + i * 400;
                var curve = PathEditingOperations.Cubic(new(x, 10400), new(x + 400, 10400), new(x + 400, 10000));
                var expression = new Mock<IExpression<BtlPoint>>();
                expression.Setup(e => e.Evaluate(It.IsAny<ExpressionContext>()))
                    .Returns(() => { evaluations++; return new BtlPoint(x, 10400); });
                expression.SetupGet(e => e.ExpressionString).Returns("distant point");
                curve.ControlPoint1.Expression = expression.Object;
                editor.Figure.Segments.Add(curve);
            }
        }
        editor.View.Matrix = Matrix.CreateScale(64, 64);
        HeadlessTestHelpers.Render(3);
        evaluations = 0;
        editor.MouseMove(new(100, 100));
        Assert.That(evaluations, Is.LessThanOrEqualTo(count * 4), "far edges must not be adaptively evaluated");
    }

    [AvaloniaTest]
    public async Task Pen_inserts_on_edge_without_changing_existing_shape()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(4));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Value, Is.EqualTo(new BtlPoint(130, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().Value, Is.EqualTo(new BtlPoint(180, 100)));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    [TestCase("arc")]
    [TestCase("closing-edge")]
    [TestCase("unrelated-animation")]
    [TestCase("zero-weight-conic")]
    [TestCase("long-edge")]
    public async Task Pen_inserts_on_every_visible_static_edge_and_undo_restores_it(string kind)
    {
        using var editor = await Fixture.Create(empty: true);
        Point click = new(180, 100);
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.StartPoint.Value = new(80, 100);
            switch (kind)
            {
                case "arc":
                    editor.Figure.Segments.Add(new ArcSegment
                    {
                        Radius = { Value = new(100, 80) },
                        Point = { Value = new(280, 100) }
                    });
                    click = new(180, 20);
                    break;
                case "closing-edge":
                    editor.Figure.IsClosed.Value = true;
                    editor.Figure.Segments.Add(new LineSegment(280, 100));
                    editor.Figure.Segments.Add(new LineSegment(280, 260));
                    click = new(180, 180);
                    break;
                case "zero-weight-conic":
                    editor.Figure.Segments.Add(new ConicSegment(new(180, 20), new(280, 100), 0));
                    break;
                case "long-edge":
                    editor.Figure.Segments.Add(new LineSegment(100080, 100));
                    click = new(120, 100);
                    break;
                default:
                    editor.Figure.Segments.Add(new LineSegment(280, 100));
                    var animated = new LineSegment(280, 260);
                    var animation = new KeyFrameAnimation<BtlPoint>();
                    animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = { Value = new(280, 260) } });
                    animated.GetEndPoint().SetKeyFrameAnimation(animation);
                    editor.Figure.Segments.Add(animated);
                    break;
            }
        }
        HeadlessTestHelpers.Render(3);
        var original = editor.Figure.Segments.ToArray();
        editor.Key(Key.P);
        editor.Click(click.X, click.Y);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(original.Length + 1));
        var inserted = editor.Model.SelectedOperation.Value!;
        Assert.That(editor.Figure.Segments.IndexOf(inserted), Is.EqualTo(kind == "closing-edge" ? original.Length : 0),
            "the point must split the clicked edge, not append an unrelated edge");
        BtlPoint point = inserted.GetEndPoint().Value;
        Assert.That(point.X, Is.EqualTo(click.X).Within(.1));
        Assert.That(point.Y, Is.EqualTo(click.Y).Within(.1));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments, Is.EqualTo(original));
        editor.Editor.HistoryManager.Redo();
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(original.Length + 1));
    }

    [AvaloniaTest]
    [TestCase("cubic")]
    [TestCase("quadratic")]
    [TestCase("conic")]
    public async Task Pen_inserts_at_the_cursor_on_a_straight_curve_with_uneven_parameter_speed(string kind)
    {
        using var editor = await Fixture.Create(empty: true);
        BtlPoint start = new(80, 100), end = new(280, 100);
        PathSegment curve = kind switch
        {
            "quadratic" => new QuadraticBezierSegment(start, end),
            "conic" => new ConicSegment(start, end, .25f),
            _ => PathEditingOperations.Cubic(start, end, end)
        };
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.StartPoint.Value = start;
            editor.Figure.Segments.Add(curve);
        }
        HeadlessTestHelpers.Render(3);
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(2));
        Assert.That(editor.Figure.Segments[1], Is.SameAs(curve));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value.X, Is.EqualTo(130).Within(.5));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value.Y, Is.EqualTo(100).Within(.001));
    }

    [AvaloniaTest]
    public async Task Pen_inserts_on_a_zoomed_bezier_between_hit_test_samples()
    {
        using var editor = await Fixture.Create(empty: true);
        var curve = PathEditingOperations.Cubic(new(0, 2000), new(2000, 2000), new(2000, 0));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.StartPoint.Value = default;
            editor.Figure.Segments.Add(curve);
        }
        BtlPoint point = PathEditingOperations.Evaluate(default, curve, 32.5f / 64, CompositionContext.Default);
        editor.View.Matrix = Matrix.CreateScale(64, 64) * Matrix.CreateTranslation(180 - point.X * 64, 100 - point.Y * 64);
        HeadlessTestHelpers.Render(3);
        editor.Key(Key.P);
        editor.Click(180, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(2));
        Assert.That(editor.Figure.Segments[1], Is.SameAs(curve), "split the curve instead of extending the path");
        var inserted = editor.Figure.Segments[0].GetEndPoint().Value;
        Assert.That(inserted.X, Is.EqualTo(point.X).Within(.01));
        Assert.That(inserted.Y, Is.EqualTo(point.Y).Within(.01));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments, Is.EqualTo(new[] { curve }));
    }

    [AvaloniaTest]
    public async Task Bend_and_alt_handle_drag_preserve_the_opposite_handle()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        var incoming = (CubicBezierSegment)editor.Figure.Segments[1];
        var outgoing = (CubicBezierSegment)editor.Figure.Segments[2];
        Assert.That(incoming.ControlPoint2.Value, Is.Not.EqualTo(new BtlPoint(180, 100)));
        BtlPoint original = outgoing.ControlPoint1.Value;
        BtlPoint point = incoming.ControlPoint2.Value;
        editor.Key(Key.V);
        editor.Drag(new(point.X, point.Y), new(point.X, point.Y - 30), RawInputModifiers.Alt);
        Assert.That(outgoing.ControlPoint1.Value, Is.EqualTo(original));
        editor.Drag(new(point.X, point.Y - 30), new(point.X - 15, point.Y - 35));
        Assert.That(outgoing.ControlPoint1.Value, Is.Not.EqualTo(original));
        Assert.That(editor.Model.Symmetry.Value, Is.True);
    }

    [AvaloniaTest]
    public async Task Preview_overlay_uses_the_same_selection_nudge_and_pan_operations()
    {
        using var editor = await Fixture.Create();
        var model = editor.Editor.Player.PathEditor;
        model.FigureContext.Value = editor.Model.FigureContext.Value;
        var overlay = new PathEditorView { DataContext = model };
        Vector pan = default;
        overlay.PanViewport = delta => pan += delta;
        editor.Window.Content = overlay;
        HeadlessTestHelpers.Render(3);
        var canvas = overlay.FindControl<Canvas>("canvas")!;
        Thumb thumb = canvas.Children.OfType<Thumb>().Single(t => ReferenceEquals(t.DataContext, editor.Figure.Segments[0]));
        editor.ClickControl(thumb);
        Assert.That(overlay.GetSelectedAnchors(), Has.Length.EqualTo(1));
        editor.Window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        editor.Window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(90, 100)));
        editor.MouseDown(new(350, 280), MouseButton.Middle);
        editor.MouseMove(new(370, 300), RawInputModifiers.MiddleMouseButton);
        editor.MouseUp(new(370, 300), MouseButton.Middle);
        Assert.That(pan, Is.EqualTo(new Vector(20, 20)));
        overlay.DataContext = null;
        model.FigureContext.Value = null;
    }

    [AvaloniaTest]
    public async Task Selecting_a_segment_from_properties_updates_canvas_and_keyboard_selection()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        editor.Model.SelectedOperation.Value = editor.Figure.Segments[2];
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[2] }));
        editor.Key(Key.Right);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().Value, Is.EqualTo(new BtlPoint(281, 180)));
    }

    [AvaloniaTest]
    public async Task Point_drag_respects_a_rotated_and_zoomed_canvas()
    {
        using var editor = await Fixture.Create();
        editor.View.Matrix = Matrix.CreateRotation(.2) * Matrix.CreateScale(1.5, 1.5) * Matrix.CreateTranslation(20, 30);
        HeadlessTestHelpers.Render(3);
        Point start = editor.View.Matrix.Transform(new Point(80, 100));
        Point end = editor.View.Matrix.Transform(new Point(95, 110));
        editor.Drag(start, end);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(95, 110)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task Animated_nudge_changes_the_current_keyframe_and_undo_restores_it()
    {
        using var editor = await Fixture.Create();
        var first = new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = new(80, 100) } };
        var second = new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = new(110, 130) } };
        var animation = new KeyFrameAnimation<BtlPoint> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(first);
        animation.KeyFrames.Add(second);
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.Segments[0].GetEndPoint().SetKeyFrameAnimation(animation);
        HeadlessTestHelpers.Render(3);
        editor.Click(80, 100);
        editor.Key(Key.Right, RawInputModifiers.Shift);
        Assert.That(first.Value.Value, Is.EqualTo(new BtlPoint(90, 100)));
        Assert.That(second.Value.Value, Is.EqualTo(new BtlPoint(110, 130)));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().GetKeyFrameAnimation(), Is.SameAs(animation));
        editor.Editor.HistoryManager.Undo();
        Assert.That(first.Value.Value, Is.EqualTo(new BtlPoint(80, 100)));
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Single_keyframe_control_allows_insertion_on_each_segment(int index)
    {
        var figure = new PathFigure { IsClosed = { Value = true } };
        figure.Segments.Add(PathEditingOperations.Cubic(new(589.81f, 56.57f), new(392.78f, 134.8f), new(276.76f, 115.35f)));
        figure.Segments.Add(PathEditingOperations.Cubic(new(191.89f, 101.12f), new(130.91277f, -95.69183f), new(208.2f, -133.51f)));
        var third = PathEditingOperations.Cubic(new(323.6624f, -190.00803f), new(437.95f, -115.68f), new(580.59f, -33.32f));
        var singleKey = new KeyFrameAnimation<BtlPoint>();
        singleKey.KeyFrames.Add(new KeyFrame<BtlPoint>
        {
            Value = { Value = new(571.368f, -123.206566f) },
            KeyTime = { Value = MediaTime.Zero },
            Easing = { Value = new SplineEasing(0, 0, 1, 1) }
        });
        third.ControlPoint2.SetKeyFrameAnimation(singleKey);
        figure.Segments.Add(third);
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Window.Width = 900;
        editor.Window.Height = 600;
        editor.View.Matrix = Matrix.CreateScale(1.2, 1.2) * Matrix.CreateTranslation(-100, 250);
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var context = CompositionContext.Default;
        var original = (CubicBezierSegment)figure.Segments[index];
        BtlPoint start = PathEditingOperations.Start(figure, index, context);
        BtlPoint[] expected = Enumerable.Range(0, 101)
            .Select(i => PathEditingOperations.Evaluate(start, original, i / 100f, context)).ToArray();
        var baseCurve = BaseCurve(original);
        BtlPoint[] expectedBase = Enumerable.Range(0, 101)
            .Select(i => PathEditingOperations.Evaluate(start, baseCurve, i / 100f, context)).ToArray();
        var animation = ((CubicBezierSegment)figure.Segments[2]).ControlPoint2.GetKeyFrameAnimation()!;
        var key = (KeyFrame<BtlPoint>)((KeyFrameAnimation<BtlPoint>)animation).KeyFrames.Single();
        var keyId = key.Id.Value;
        var easing = key.Easing.Value;
        var before = CoreSerializer.SerializeToJsonObject(figure);
        Point click = editor.View.Matrix.Transform(new Point(expected[50].X, expected[50].Y));
        editor.Key(Key.P);
        editor.Click(click.X, click.Y);
        Assert.That(figure.Segments, Has.Count.EqualTo(4), "the keyed segment must not silently reject insertion");
        var inserted = figure.Segments[index];
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(inserted));
        Assert.That(figure.Segments[index + 1], Is.SameAs(original));
        if (index == 2) Assert.That(original.ControlPoint2.GetKeyFrameAnimation(), Is.SameAs(animation));
        Assert.That(key.Id.Value, Is.EqualTo(keyId));
        Assert.That(key.Easing.Value, Is.SameAs(easing));
        Assert.That(key.KeyTime.Value, Is.EqualTo(MediaTime.Zero));
        var animatedProperties = figure.Segments.SelectMany(s => PathEditorHelper.GetControlPointProperties(s).Append(s.GetEndPoint()))
            .Select(p => p.GetKeyFrameAnimation()).OfType<KeyFrameAnimation<BtlPoint>>().ToArray();
        Assert.That(animatedProperties.Select(a => a.Id.Value).Distinct().Count(), Is.EqualTo(animatedProperties.Length));
        Assert.That(animatedProperties.SelectMany(a => a.KeyFrames).Select(k => k.Id.Value).Distinct().Count(), Is.EqualTo(animatedProperties.Length));
        if (index == 2)
        {
            Assert.That(animatedProperties, Has.Length.EqualTo(4));
            var derived = ((KeyFrameAnimation<BtlPoint>)inserted.GetEndPoint().GetKeyFrameAnimation()!).KeyFrames[0];
            Assert.That(derived.Easing.Value, Is.Not.SameAs(easing));
            Assert.That(derived.KeyTime.Value, Is.EqualTo(key.KeyTime.Value));
        }
        var leftBase = BaseCurve((CubicBezierSegment)inserted);
        var rightBase = BaseCurve(original);
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            var actual = t <= .5f ? PathEditingOperations.Evaluate(start, leftBase, t * 2, context)
                : PathEditingOperations.Evaluate(leftBase.EndPoint.Value, rightBase, (t - .5f) * 2, context);
            Assert.That(((Beutl.Graphics.Vector)(actual - expectedBase[i])).Length, Is.LessThan(.02), "base values must not be replaced with keyframe values");
        }
        foreach (var time in new[] { -1, 0, 1, 5 })
        {
            context = new CompositionContext(MediaTime.FromSeconds(time));
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                var actual = t <= .5f ? PathEditingOperations.Evaluate(start, inserted, t * 2, context)
                    : PathEditingOperations.Evaluate(inserted.GetEndPoint().Evaluate(context), original, (t - .5f) * 2, context);
                Assert.That(((Beutl.Graphics.Vector)(actual - expected[i])).Length, Is.LessThan(.02));
            }
        }
        var after = CoreSerializer.SerializeToJsonObject(figure);
        var restored = (PathFigure)CoreSerializer.DeserializeFromJsonObject(after, typeof(PathFigure));
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(restored), after), Is.True);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(figure), before), Is.True);
        editor.Editor.HistoryManager.Redo();
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(figure), after), Is.True);
        if (index == 2)
        {
            editor.View.Refresh();
            HeadlessTestHelpers.Render(3);
            BtlPoint again = PathEditingOperations.Evaluate(start, inserted, .5f, context);
            Point nextClick = editor.View.Matrix.Transform(new Point(again.X, again.Y));
            editor.Click(nextClick.X, nextClick.Y);
            Assert.That(figure.Segments, Has.Count.EqualTo(5), "a derived keyed segment must remain splittable");
            editor.Editor.HistoryManager.Undo();
            Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(figure), after), Is.True);
        }

        static CubicBezierSegment BaseCurve(CubicBezierSegment curve) => PathEditingOperations.Cubic(
            curve.ControlPoint1.Value, curve.ControlPoint2.Value, curve.EndPoint.Value);
    }

    [AvaloniaTest]
    public async Task Pen_on_a_time_varying_edge_does_not_append_an_unrelated_point()
    {
        using var editor = await Fixture.Create();
        var animation = new KeyFrameAnimation<BtlPoint>();
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = new(180, 100) } });
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = new(200, 120) } });
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.Segments[1].GetEndPoint().SetKeyFrameAnimation(animation);
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().GetKeyFrameAnimation(), Is.SameAs(animation));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public async Task Escape_cancels_a_point_drag_or_unfinished_pen_gesture()
    {
        using var editor = await Fixture.Create();
        editor.MouseDown(new(80, 100), MouseButton.Left);
        editor.MouseMove(new(110, 120), RawInputModifiers.LeftMouseButton);
        editor.Key(Key.Escape);
        editor.MouseUp(new(110, 120), MouseButton.Left);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        Assert.That(editor.View.SkipUpdatePosition, Is.False);
        editor.Key(Key.P);
        editor.MouseDown(new(350, 220), MouseButton.Left);
        editor.MouseMove(new(370, 230), RawInputModifiers.LeftMouseButton);
        editor.Key(Key.Escape);
        editor.MouseUp(new(370, 230), MouseButton.Left);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Escape_keeps_a_nudge_made_just_before_the_gesture_in_its_own_entry(bool pen)
    {
        using var editor = await Fixture.Create();
        Element element = editor.Editor.Scene.Children.Single();
        MediaTime start = element.Start.Value;
        Point from = pen ? new(350, 220) : new(80, 100);
        if (pen) editor.Key(Key.P);

        ((IElementNudgeService)editor.Editor.GetService(typeof(IElementNudgeService))!)
            .Nudge(editor.Editor.Scene, [element], 1);
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(from + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
        editor.Key(Key.Escape);
        editor.MouseUp(from + new Vector(30, 20), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
            Assert.That(editor.Figure.Segments[0].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
            Assert.That(element.Start.Value, Is.GreaterThan(start));
            Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
            Assert.That(editor.Editor.HistoryManager.PeekUndo()!.Name, Is.EqualTo("CommandNames.MoveElement"));
        });
    }

    [AvaloniaTest]
    public async Task Finishing_path_editing_clears_the_inspector_and_shows_the_empty_state()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        editor.Model.FigureContext.Value = null;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.View.FindControl<Canvas>("canvas")!.Children.OfType<Thumb>(), Is.Empty);
        Assert.That(editor.View.FindControl<Vector2Editor<float>>("PointPosition")!.IsEnabled, Is.False);
        Assert.That(editor.View.FindControl<Avalonia.Controls.TextBlock>("EmptyHint")!.IsVisible, Is.True);
    }

    [AvaloniaTest]
    public async Task Coordinate_fields_move_selected_points_and_fit_keeps_them_visible()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        editor.Click(180, 100, RawInputModifiers.Shift);
        var positionEditor = editor.View.FindControl<Vector2Editor<float>>("PointPosition")!;
        Assert.That(positionEditor.FirstValue, Is.EqualTo(130));
        var input = positionEditor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("150");
        editor.View.Focus();
        HeadlessTestHelpers.Render(2);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Value.X, Is.EqualTo(100));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Value.X, Is.EqualTo(200));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Key(Key.D2, RawInputModifiers.Shift);
        foreach (var thumb in editor.View.GetSelectedAnchors())
        {
            var position = PathEditorHelper.GetCanvasPosition(thumb);
            Assert.That(position.X, Is.InRange(30, editor.View.Bounds.Width - 30));
            Assert.That(position.Y, Is.InRange(30, editor.View.Bounds.Height - 30));
        }
    }

    [AvaloniaTest]
    public async Task Bend_drag_on_an_edge_follows_the_pointer_and_undo_restores_the_line()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Drag(new(130, 100), new(130, 145));
        var segment = editor.Figure.Segments[1];
        Assert.That(segment, Is.TypeOf<CubicBezierSegment>());
        var middle = PathEditingOperations.Evaluate(new BtlPoint(80, 100), segment, .5f, new CompositionContext(MediaTime.Zero));
        Assert.That(middle.Y, Is.EqualTo(145).Within(.1));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments[1], Is.TypeOf<LineSegment>());
    }

    [AvaloniaTest]
    public async Task Point_inspector_uses_only_related_control_points_and_standard_menus()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var curve = (CubicBezierSegment)editor.Figure.Segments[1];
        var list = editor.View.FindControl<ItemsControl>("PointPropertyList")!;
        var properties = editor.Model.PointProperties.Value;
        var controls = list.GetVisualDescendants().OfType<Vector2Editor<float>>().ToArray();
        var next = (CubicBezierSegment)editor.Figure.Segments[2];
        Assert.That(controls.Select(c => ((PropertyEditorViewModel)c.DataContext!).PropertyAdapter.GetEngineProperty()),
            Is.EqualTo(new Beutl.IProperty[] { curve.EndPoint, curve.ControlPoint2, next.ControlPoint1 }));
        Assert.That(controls.Select(c => c.Header), Is.EqualTo(new[]
        {
            GraphicsStrings.Position, Strings.PathEditor_IncomingControlPoint, Strings.PathEditor_OutgoingControlPoint
        }));
        foreach (var control in controls)
        {
            Assert.That(control.EditorStyle, Is.EqualTo(PropertyEditorStyle.Normal));
            Assert.That(control.MenuContent, Is.TypeOf<PropertyEditorMenu>());
            Assert.That(control.Margin, Is.EqualTo(new Thickness(4, 0)));
            Assert.That(((PropertyEditorViewModel)control.DataContext!).GetService(typeof(Element)),
                Is.SameAs(editor.Model.Element.Value));
        }
        Assert.That(editor.View.FindControl<Border>("PointProperties")!.Padding.Left, Is.Zero);
        Assert.That(editor.View.FindControl<Vector2Editor<float>>("PointPosition")!.IsVisible, Is.False);

        var controlPoint = controls.Single(c => ReferenceEquals(
            ((PropertyEditorViewModel)c.DataContext!).PropertyAdapter.GetEngineProperty(), curve.ControlPoint2));
        var context = (PropertyEditorViewModel)controlPoint.DataContext!;
        var input = controlPoint.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        int undo = editor.Editor.HistoryManager.UndoCount;
        BtlPoint previous = curve.ControlPoint2.Value;
        input.BringIntoView();
        HeadlessTestHelpers.Render(2);
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("115");
        editor.View.Focus();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.Value, Is.EqualTo(new BtlPoint(115, previous.Y)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        Assert.That(editor.Model.PointProperties.Value, Is.SameAs(properties));
        var controlThumb = editor.View.FindThumb(curve, curve.ControlPoint2)!;
        Assert.That(PathEditorHelper.GetCanvasPosition(controlThumb).X, Is.EqualTo(115));

        var menu = (PropertyEditorMenu)controlPoint.MenuContent!;
        var button = menu.GetVisualDescendants().OfType<Button>().Single();
        editor.ClickControl(button);
        var flyout = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(flyout.IsOpen, Is.True);
        var reset = flyout.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Reset);
        Assert.That(reset.Command, Is.Not.Null);
        Assert.That(reset.IsEnabled, Is.True);
        editor.ClickControl(reset);
        Assert.That(curve.ControlPoint2.Value, Is.EqualTo(default(BtlPoint)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(undo + 2));
        editor.Editor.HistoryManager.Undo();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.Value, Is.EqualTo(new BtlPoint(115, previous.Y)));

        context.PrepareToEditAnimation();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.GetKeyFrameAnimation(), Is.Not.Null);
        Assert.That(controlPoint.KeyFrameCount, Is.GreaterThan(0));
        Assert.That(controlPoint.MenuContent, Is.SameAs(menu));
        editor.Click(80, 100);
        Assert.That(context.IsDisposed, Is.True);
        Assert.That(((PropertyEditorViewModel)editor.Model.PointProperties.Value[0]).PropertyAdapter.GetEngineProperty(),
            Is.SameAs(editor.Figure.Segments[0].GetEndPoint()));
    }

    [AvaloniaTest]
    public async Task Clicking_an_outgoing_handle_keeps_its_anchor_and_related_properties()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var anchor = editor.Figure.Segments[1];
        var outgoing = (CubicBezierSegment)editor.Figure.Segments[2];
        var properties = editor.Model.PointProperties.Value;
        BtlPoint point = outgoing.ControlPoint1.Value;
        editor.Click(point.X, point.Y);
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(anchor));
        Assert.That(editor.Model.PointProperties.Value, Is.SameAs(properties));
        Assert.That(((PropertyEditorViewModel)properties[0]).PropertyAdapter.GetEngineProperty(), Is.SameAs(anchor.GetEndPoint()));
        Assert.That(((PropertyEditorViewModel)properties[2]).PropertyAdapter.GetEngineProperty(), Is.SameAs(outgoing.ControlPoint1));
    }

    [AvaloniaTest]
    public async Task Editing_the_outgoing_control_does_not_edit_unrelated_points_in_either_segment()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[1];
        var next = (CubicBezierSegment)editor.Figure.Segments[2];
        BtlPoint unrelated = anchor.ControlPoint1.Value;
        BtlPoint otherHandle = next.ControlPoint2.Value;
        BtlPoint previous = next.ControlPoint1.Value;
        var control = editor.View.FindControl<ItemsControl>("PointPropertyList")!
            .GetVisualDescendants().OfType<Vector2Editor<float>>().Last();
        var input = control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        input.BringIntoView();
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("220");
        editor.View.Focus();
        HeadlessTestHelpers.Render(3);
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(new BtlPoint(220, previous.Y)));
        Assert.That(anchor.ControlPoint1.Value, Is.EqualTo(unrelated));
        Assert.That(next.ControlPoint2.Value, Is.EqualTo(otherHandle));
        Assert.That(anchor.EndPoint.Value, Is.EqualTo(new BtlPoint(180, 100)));
        Assert.That(next.EndPoint.Value, Is.EqualTo(new BtlPoint(280, 180)));
        editor.Editor.HistoryManager.Undo();
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(previous));
    }

    [AvaloniaTest]
    public async Task A_handle_click_changes_the_active_point_without_losing_the_group_selection()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        editor.Click(80, 100, RawInputModifiers.Shift);
        var next = (CubicBezierSegment)editor.Figure.Segments[1];
        var point = next.ControlPoint1.Value;
        editor.Click(point.X, point.Y);
        Assert.That(editor.View.GetSelectedAnchors(), Has.Length.EqualTo(2));
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(editor.Figure.Segments[0]));
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(((PropertyEditorViewModel)editor.Model.PointProperties.Value[1]).PropertyAdapter.GetEngineProperty(),
            Is.SameAs(next.ControlPoint1));
    }

    [AvaloniaTest]
    public async Task Point_inspector_updates_when_the_adjacent_segment_changes_and_undo_restores_it()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[1];
        var next = (CubicBezierSegment)editor.Figure.Segments[2];
        var outgoingEditor = (PropertyEditorViewModel)editor.Model.PointProperties.Value[2];
        editor.Editor.HistoryManager.ExecuteInTransaction(() =>
            editor.Figure.Segments[2] = new LineSegment(280, 180));
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(anchor));
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(outgoingEditor.IsDisposed, Is.True);
        editor.Editor.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(3));
        Assert.That(((PropertyEditorViewModel)editor.Model.PointProperties.Value[2]).PropertyAdapter.GetEngineProperty(),
            Is.SameAs(next.ControlPoint1));
    }

    [Test]
    public void Point_relationships_respect_open_endpoints_and_closed_path_representation()
    {
        var figure = new PathFigure();
        var first = PathEditingOperations.Cubic(new(10, 20), new(30, 40), new(50, 60));
        var middle = PathEditingOperations.Cubic(new(70, 80), new(90, 100), new(110, 120));
        var last = PathEditingOperations.Cubic(new(130, 140), new(150, 160), new(170, 180));
        figure.Segments.Add(first);
        figure.Segments.Add(middle);
        figure.Segments.Add(last);
        Check(first, first.EndPoint, middle.ControlPoint1);
        Check(middle, middle.EndPoint, middle.ControlPoint2, last.ControlPoint1);
        Check(last, last.EndPoint, last.ControlPoint2);
        figure.IsClosed.Value = true;
        Check(first, first.EndPoint, first.ControlPoint2, middle.ControlPoint1);
        Check(last, last.EndPoint, last.ControlPoint2, first.ControlPoint1);
        figure.StartPoint.Value = new(-10, -10);
        Check(last, last.EndPoint, last.ControlPoint2);
        figure.IsClosed.Value = false;
        Check(first, first.EndPoint, first.ControlPoint2, middle.ControlPoint1);

        void Check(PathSegment anchor, params Beutl.IProperty<BtlPoint>[] expected)
        {
            var actual = PathPointProperties.Get(figure, anchor, new CompositionContext(MediaTime.Zero));
            Assert.That(actual.Select(p => p.Property), Is.EqualTo(expected));
        }
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void Shared_quadratic_and_conic_control_points_are_listed_once(bool conic)
    {
        var figure = new PathFigure();
        PathSegment segment = conic ? new ConicSegment(new(30, 50), new(90, 10), .5f)
            : new QuadraticBezierSegment(new(30, 50), new(90, 10));
        figure.Segments.Add(segment);
        figure.IsClosed.Value = true;
        var properties = PathPointProperties.Get(figure, segment, new CompositionContext(MediaTime.Zero));
        Assert.That(properties.Select(p => p.Property), Is.EqualTo(new[]
        {
            segment.GetEndPoint(), PathEditorHelper.GetControlPointProperties(segment).Single()
        }));
        Assert.That(properties[1].Role, Is.EqualTo(PathPointPropertyRole.Shared));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Bend_creates_real_endpoint_handles_that_can_be_dragged_and_undone(bool last, bool independent)
    {
        using var editor = await Fixture.CreateCollapsed(last);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[last ? 2 : 0];
        var next = (CubicBezierSegment)editor.Figure.Segments[last ? 0 : 1];
        BtlPoint point = anchor.EndPoint.Value;
        var incoming = editor.View.FindThumb(anchor, anchor.ControlPoint2)!;
        var outgoing = editor.View.FindThumb(next, next.ControlPoint1)!;
        var anchorThumb = editor.View.FindAnchorThumb(anchor)!;
        Point position = PathEditorHelper.GetCanvasPosition(anchorThumb);
        Assert.That(PathEditorHelper.GetCanvasPosition(incoming), Is.EqualTo(position));
        Assert.That(PathEditorHelper.GetCanvasPosition(outgoing), Is.EqualTo(position));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);

        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        editor.Key(Key.V);
        BtlPoint bentIncoming = anchor.ControlPoint2.Value;
        BtlPoint bentOutgoing = next.ControlPoint1.Value;
        Assert.That(anchor.EndPoint.Value, Is.EqualTo(point));
        Assert.That(bentIncoming, Is.Not.EqualTo(point));
        Assert.That(bentOutgoing, Is.Not.EqualTo(point));
        Assert.That(incoming.IsVisible && outgoing.IsVisible, Is.True);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        Point from = PathEditorHelper.GetCanvasPosition(incoming);
        Point to = from + new Vector(-26, -15);
        Point expected = editor.View.Matrix.Invert().Transform(to);
        editor.Drag(from, to, independent ? RawInputModifiers.Alt : RawInputModifiers.None);
        Assert.That(anchor.EndPoint.Value, Is.EqualTo(point));
        Assert.That(anchor.ControlPoint2.Value.X, Is.EqualTo(expected.X).Within(.02));
        Assert.That(anchor.ControlPoint2.Value.Y, Is.EqualTo(expected.Y).Within(.02));
        Assert.That(next.ControlPoint1.Value, independent ? Is.EqualTo(bentOutgoing) : Is.Not.EqualTo(bentOutgoing));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(2));
        editor.Editor.HistoryManager.Undo();
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(bentIncoming));
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(bentOutgoing));
        editor.Editor.HistoryManager.Undo();
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(point));
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(point));
        Assert.That(PathEditorHelper.GetCanvasPosition(incoming), Is.EqualTo(position));
        Assert.That(PathEditorHelper.GetCanvasPosition(outgoing), Is.EqualTo(position));
    }

    [AvaloniaTest]
    [TestCase("static")]
    [TestCase("animated-anchor")]
    [TestCase("animated-handles")]
    [TestCase("coincident-neighbors")]
    [TestCase("nearly-collapsed")]
    public async Task Bend_toolbar_click_expands_collapsed_handles(string scenario)
    {
        using var editor = await Fixture.CreateCollapsed(false);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        var next = (CubicBezierSegment)editor.Figure.Segments[1];
        BtlPoint point = anchor.EndPoint.Value;
        var context = new CompositionContext(MediaTime.Zero);
        var animation = new KeyFrameAnimation<BtlPoint>();
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = point } });
        var later = new KeyFrame<BtlPoint> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = new(220, -70) } };
        animation.KeyFrames.Add(later);
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (scenario == "animated-anchor") anchor.EndPoint.SetKeyFrameAnimation(animation);
            if (scenario == "animated-handles") anchor.ControlPoint2.SetKeyFrameAnimation(animation);
            if (scenario == "coincident-neighbors")
                editor.Figure.Segments[^1].GetEndPoint().Value = next.EndPoint.Value;
            if (scenario == "nearly-collapsed")
                anchor.ControlPoint2.Value = point + new Beutl.Graphics.Vector(.001f, -.001f);
        }
        BtlPoint originalIncoming = anchor.ControlPoint2.Evaluate(context);
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var tool = editor.View.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PathTools")
            .Children.OfType<RadioButton>().ElementAt(2);
        editor.ClickControl(tool);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Bend));
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.Evaluate(context), Is.Not.EqualTo(point), "incoming handle must expand");
        Assert.That(next.ControlPoint1.Evaluate(context), Is.Not.EqualTo(point), "outgoing handle must expand");
        Assert.That(anchor.EndPoint.Evaluate(context), Is.EqualTo(point));
        Assert.That(later.Value.Value, Is.EqualTo(new BtlPoint(220, -70)), "a different keyframe must stay unchanged");
        foreach (var property in PathPointProperties.Get(editor.Figure, anchor, context).Skip(1))
        {
            var thumb = editor.View.FindThumb(property.Owner, property.Property)!;
            Assert.That(thumb.IsVisible, Is.True);
            Assert.That(((Vector)(PathEditorHelper.GetCanvasPosition(thumb) - position)).Length, Is.GreaterThan(8));
        }
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(anchor.ControlPoint2.Evaluate(context), Is.EqualTo(originalIncoming));
        Assert.That(next.ControlPoint1.Evaluate(context), Is.EqualTo(point));
        if (scenario == "animated-anchor") Assert.That(anchor.EndPoint.GetKeyFrameAnimation(), Is.SameAs(animation));
        if (scenario == "animated-handles") Assert.That(anchor.ControlPoint2.GetKeyFrameAnimation(), Is.SameAs(animation));
    }

    [AvaloniaTest]
    public async Task Bend_at_an_explicit_closed_endpoint_toggles_only_its_own_handle()
    {
        using var editor = await Fixture.CreateCollapsed(last: true);
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.StartPoint.Value = new(190, 160);
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[^1];
        var first = (CubicBezierSegment)editor.Figure.Segments[0];
        BtlPoint unrelated = first.ControlPoint1.Value;
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.Value, Is.Not.EqualTo(anchor.EndPoint.Value));
        Assert.That(first.ControlPoint1.Value, Is.EqualTo(unrelated));
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(anchor.EndPoint.Value));
        Assert.That(first.ControlPoint1.Value, Is.EqualTo(unrelated));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task Selecting_a_corner_does_not_expand_it_and_escape_cancels_only_the_handle_drag()
    {
        using var editor = await Fixture.CreateCollapsed(false);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        var next = (CubicBezierSegment)editor.Figure.Segments[1];
        BtlPoint original = anchor.EndPoint.Value;
        var thumb = editor.View.FindThumb(anchor, anchor.ControlPoint2)!;
        Point position = PathEditorHelper.GetCanvasPosition(thumb);
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(original));
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(original));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        editor.Key(Key.V);
        BtlPoint incoming = anchor.ControlPoint2.Value;
        BtlPoint outgoing = next.ControlPoint1.Value;
        Point from = PathEditorHelper.GetCanvasPosition(thumb);
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(from + new Vector(-30, -10), RawInputModifiers.LeftMouseButton);
        editor.Key(Key.Escape);
        editor.MouseUp(from + new Vector(-30, -10), MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(incoming));
        Assert.That(next.ControlPoint1.Value, Is.EqualTo(outgoing));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        Assert.That(editor.Editor.HistoryManager.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    public async Task Bend_handles_under_zoom_and_rotation_follow_their_actual_coordinates()
    {
        using var editor = await Fixture.CreateCollapsed(false);
        editor.View.Matrix = Matrix.CreateRotation(.2) * Matrix.CreateScale(1.3, 1.3) * Matrix.CreateTranslation(-130, 150);
        HeadlessTestHelpers.Render(3);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        editor.Key(Key.V);
        var thumb = editor.View.FindThumb(anchor, anchor.ControlPoint2)!;
        Point from = PathEditorHelper.GetCanvasPosition(thumb);
        Point to = from + new Vector(-30, 15);
        Point expected = editor.View.Matrix.Invert().Transform(to);
        editor.Drag(from, to);
        Assert.That(anchor.ControlPoint2.Value.X, Is.EqualTo(expected.X).Within(.02));
        Assert.That(anchor.ControlPoint2.Value.Y, Is.EqualTo(expected.Y).Within(.02));
        Point actual = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
            editor.View.FindControl<Canvas>("canvas")!)!.Value;
        Assert.That(actual.X, Is.EqualTo(to.X).Within(.51));
        Assert.That(actual.Y, Is.EqualTo(to.Y).Within(.51));
    }

    [AvaloniaTest]
    public async Task Preview_overlay_keeps_corner_handles_at_the_anchor_until_bend_is_used()
    {
        using var editor = await Fixture.CreateCollapsed(false);
        var model = editor.Editor.Player.PathEditor;
        model.FigureContext.Value = editor.Model.FigureContext.Value;
        var overlay = new PathEditorView { DataContext = model };
        editor.Window.Content = overlay;
        HeadlessTestHelpers.Render(3);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        model.SelectedOperation.Value = anchor;
        HeadlessTestHelpers.Render(3);
        var thumb = overlay.FindThumb(anchor, anchor.ControlPoint2)!;
        var anchorThumb = overlay.GetSelectedAnchors().Single();
        Point position = PathEditorHelper.GetCanvasPosition(anchorThumb);
        Assert.That(PathEditorHelper.GetCanvasPosition(thumb), Is.EqualTo(position));
        Assert.That(anchor.ControlPoint2.Value, Is.EqualTo(anchor.EndPoint.Value));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        overlay.Tool = PathEditorTool.Bend;
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.Value, Is.Not.EqualTo(anchor.EndPoint.Value));
        Assert.That(thumb.IsVisible, Is.True);
        overlay.DataContext = null;
        model.FigureContext.Value = null;
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Selecting_open_endpoints_shows_their_canvas_handle_at_the_actual_position(bool last)
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var curve = (CubicBezierSegment)editor.Figure.Segments[last ? 2 : 1];
        var property = last ? curve.ControlPoint2 : curve.ControlPoint1;
        editor.Click(last ? 280 : 80, last ? 180 : 100);
        HeadlessTestHelpers.Render(3);
        var thumb = editor.View.FindThumb(curve, property)!;
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(thumb.IsVisible, Is.True);
        Assert.That(thumb.Bounds.Width, Is.GreaterThan(0));
        Point point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
            editor.View.FindControl<Canvas>("canvas")!)!.Value;
        Assert.That(point.X, Is.EqualTo(property.Value.X).Within(.51));
        Assert.That(point.Y, Is.EqualTo(property.Value.Y).Within(.51));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Endpoints_of_a_pen_drawn_curve_keep_visible_handles_when_reselected(bool last)
    {
        using var editor = await Fixture.Create(empty: true);
        editor.Key(Key.P);
        editor.Drag(new(80, 100), new(115, 70));
        editor.Drag(new(180, 120), new(215, 120));
        editor.Drag(new(280, 180), new(310, 150));
        editor.Key(Key.Enter);
        editor.Click(last ? 280 : 80, last ? 180 : 100);
        var segment = (CubicBezierSegment)editor.Figure.Segments[last ? 2 : 1];
        var property = last ? segment.ControlPoint2 : segment.ControlPoint1;
        Assert.That(editor.View.FindThumb(segment, property)!.IsVisible, Is.True);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task Geometry_overlay_stays_out_of_hit_testing_after_playback()
    {
        using var editor = await Fixture.Create();
        var player = (PlayerViewModel)editor.Editor.GetService(typeof(IPreviewPlayer))!;
        player.IsPlaying.Value = true;
        player.IsPlaying.Value = false;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.View.FindControl<PathGeometryControl>("view")!.IsHitTestVisible, Is.False);
    }

    [AvaloniaTest]
    public async Task Closing_a_path_adds_only_the_last_points_outgoing_handle()
    {
        using var editor = await Fixture.Create();
        CubicBezierSegment first = PathEditingOperations.Cubic(new(40, 60), new(60, 80), new(80, 100));
        CubicBezierSegment last = PathEditingOperations.Cubic(new(210, 100), new(250, 150), new(280, 180));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.Segments[0] = first;
            editor.Figure.Segments[2] = last;
        }
        HeadlessTestHelpers.Render(3);
        editor.Click(280, 180);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        editor.Figure.IsClosed.Value = true;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(3));
        Assert.That(((PropertyEditorViewModel)editor.Model.PointProperties.Value[2]).PropertyAdapter.GetEngineProperty(),
            Is.SameAs(first.ControlPoint1));
        Assert.That(editor.View.FindThumb(first, first.ControlPoint1)!.IsVisible, Is.True);
        editor.Figure.IsClosed.Value = false;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(editor.View.FindThumb(first, first.ControlPoint1)!.IsVisible, Is.False);
    }

    [AvaloniaTest]
    [TestCase(640)]
    [TestCase(320)]
    public async Task Render_single_curve_inspector_with_control_points_and_menus(int width)
    {
        using var editor = await Fixture.Create();
        editor.Window.Width = width;
        editor.Window.Height = width < 560 ? 420 : 560;
        HeadlessTestHelpers.Render(2);
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        HeadlessTestHelpers.Render(3);
        if (width < 560) editor.ClickControl(editor.View.FindControl<Button>("CompactSettingsButton")!);
        var list = editor.View.FindControl<ItemsControl>("PointPropertyList")!;
        var controls = list.GetVisualDescendants().OfType<Vector2Editor<float>>().ToArray();
        Assert.That(controls, Has.Length.EqualTo(3));
        foreach (var control in controls)
        {
            var menu = (PropertyEditorMenu)control.MenuContent!;
            Point left = control.TranslatePoint(default, list)!.Value;
            Point right = menu.TranslatePoint(new Point(menu.Bounds.Width, 0), list)!.Value;
            Assert.That(left.X, Is.EqualTo(4).Within(1));
            Assert.That(right.X, Is.LessThanOrEqualTo(list.Bounds.Width));
        }
    }

    [Test]
    [TestCase(140, 90, 0, false, true, .5f)]
    [TestCase(140, 90, 25, true, true, .3f)]
    [TestCase(140, 90, 25, true, false, .7f)]
    [TestCase(140, 90, -40, false, false, .2f)]
    [TestCase(30, 10, 35, true, true, .5f)]
    [TestCase(30, 10, -15, false, false, .3f)]
    [TestCase(0, 90, 0, false, true, .5f)]
    public void Arc_insertion_preserves_the_native_rendered_path(float rx, float ry, float rotation,
        bool large, bool clockwise, float t)
    {
        var figure = new PathFigure { StartPoint = { Value = new(80, 100) } };
        figure.Segments.Add(new ArcSegment
        {
            Radius = { Value = new(rx, ry) },
            RotationAngle = { Value = rotation },
            IsLargeArc = { Value = large },
            SweepClockwise = { Value = clockwise },
            Point = { Value = new(280, 180) }
        });
        using var before = Draw();
        Assert.That(PathEditingOperations.Split(figure, 0, t, CompositionContext.Default), Is.Not.Null);
        using var after = Draw();
        using var original = new SKPathMeasure(before.NativeObject, resScale: 100);
        using var split = new SKPathMeasure(after.NativeObject, resScale: 100);
        Assert.That(split.Length, Is.EqualTo(original.Length).Within(.05));
        for (int i = 0; i <= 100; i++)
        {
            var expected = original.GetPosition(original.Length * i / 100);
            var actual = split.GetPosition(split.Length * i / 100);
            Assert.That(SKPoint.Distance(expected, actual), Is.LessThan(.08), $"path position {i}%");
        }

        GeometryContext Draw()
        {
            var context = new GeometryContext();
            context.MoveTo(figure.StartPoint.Value);
            foreach (var segment in figure.Segments)
            {
                if (segment is ArcSegment arc)
                    context.ArcTo(arc.Radius.Value, arc.RotationAngle.Value,
                        arc.IsLargeArc.Value, arc.SweepClockwise.Value, arc.Point.Value);
                else context.LineTo(segment.GetEndPoint().Value);
            }
            return context;
        }
    }

    [Test]
    public void Cubic_split_preserves_curve_and_rejects_animated_topology_edits()
    {
        var figure = new PathFigure();
        figure.Segments.Add(new LineSegment(0, 0));
        var cubic = PathEditingOperations.Cubic(new(30, 100), new(70, -20), new(100, 60));
        figure.Segments.Add(cubic);
        var context = new CompositionContext(MediaTime.Zero);
        BtlPoint[] expected = Enumerable.Range(0, 101).Select(i => PathEditingOperations.Evaluate(default, cubic, i / 100f, context)).ToArray();
        var inserted = PathEditingOperations.Split(figure, 1, .4f, context)!;
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            var actual = t <= .4f ? PathEditingOperations.Evaluate(default, inserted, t / .4f, context)
                : PathEditingOperations.Evaluate(inserted.GetEndPoint().Value, cubic, (t - .4f) / .6f, context);
            Assert.That(((Beutl.Graphics.Vector)(actual - expected[i])).Length, Is.LessThan(.001));
        }
        cubic.EndPoint.SetKeyFrameAnimation(new KeyFrameAnimation<BtlPoint>());
        Assert.That(PathEditingOperations.Split(figure, 2, .5f, context), Is.Null);
        Assert.That(figure.Segments, Has.Count.EqualTo(3));
    }

    // A locked clip keeps its path: presses still select, and drags, keys, tools and fields change nothing.
    [AvaloniaTest]
    public async Task A_locked_clips_path_stays_as_it_is_under_drags_keys_tools_and_fields()
    {
        using var editor = await Fixture.Create();
        editor.Model.Element.Value!.IsLocked.Value = true;
        editor.Editor.HistoryManager.Commit();
        int undoCount = editor.Editor.HistoryManager.UndoCount;
        string before = CoreSerializer.SerializeToJsonObject(editor.Figure).ToJsonString();

        editor.Click(80, 100);
        editor.Click(180, 100, RawInputModifiers.Shift);
        Assert.That(editor.Selected, Has.Length.EqualTo(2), "Selecting still works.");
        editor.Drag(new(80, 100), new(120, 117));
        editor.Key(Key.Right);
        var input = editor.View.FindControl<Vector2Editor<float>>("PointPosition")!
            .GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("150");
        editor.View.Focus();
        HeadlessTestHelpers.Render(2);
        editor.Key(Key.Delete);
        editor.Key(Key.P);
        editor.Click(130, 100);
        editor.Click(300, 300);

        Assert.Multiple(() =>
        {
            Assert.That(CoreSerializer.SerializeToJsonObject(editor.Figure).ToJsonString(), Is.EqualTo(before));
            Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
        });
    }

    // A segment whose type did not load is left out of path editing: it gets no anchor, and the points around it are
    // edited as if it were not there, as it draws nothing.
    [AvaloniaTest]
    public async Task A_segment_whose_type_did_not_load_is_left_out_of_path_editing()
    {
        using var editor = await Fixture.Create();
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.Segments.Insert(1, new FallbackPathSegment());
            editor.Figure.Segments.Add(new FallbackPathSegment());
        }
        HeadlessTestHelpers.Render(3);

        editor.MouseMove(new Point(130, 104));
        editor.Key(Key.P);
        editor.Click(130, 100);
        editor.Click(300, 300);
        var appended = (CubicBezierSegment)editor.Figure.Segments[^1];
        FallbackPathSegment unloaded = editor.Figure.Segments.OfType<FallbackPathSegment>().First();

        Assert.Multiple(() =>
        {
            Assert.That(editor.View.FindControl<Canvas>("canvas")!.Children.OfType<Thumb>()
                .Any(thumb => thumb.DataContext is FallbackPathSegment), Is.False, "No anchor or handle for it.");
            Assert.That(editor.Figure.Segments.OfType<FallbackPathSegment>().Count(), Is.EqualTo(2));
            Assert.That(editor.Figure.Segments[2], Is.TypeOf<LineSegment>(), "The pen inserted a point on the edge past it.");
            Assert.That(editor.Figure.Segments[2].GetEndPoint().Value, Is.EqualTo(new BtlPoint(130, 100)));
            Assert.That(appended.ControlPoint1.Value,
                Is.EqualTo(PathEditingOperations.Lerp(new BtlPoint(280, 180), new BtlPoint(300, 300), 1f / 3)),
                "The pen continued from the last point that loaded.");
            Assert.That(PathPointProperties.Get(editor.Figure, unloaded, new CompositionContext(MediaTime.Zero)), Is.Empty,
                "A handle drag looking for its anchor passes over it.");
        });
    }

    // On a closed figure with an implicit start, the first edge past segments that did not load starts at the last point
    // that did, as the figure draws, so it can be hovered and split there.
    [AvaloniaTest]
    public async Task The_first_edge_past_segments_that_did_not_load_starts_where_a_closed_figure_draws_it()
    {
        using var editor = await Fixture.Create();
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.IsClosed.Value = true;
            editor.Figure.Segments.Insert(0, new FallbackPathSegment());
        }
        HeadlessTestHelpers.Render(3);

        editor.MouseMove(new Point(180, 140));
        editor.Key(Key.P);
        editor.Click(180, 140);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Figure.Segments, Has.Count.EqualTo(5));
            Assert.That(editor.Figure.Segments[1], Is.TypeOf<LineSegment>(), "The pen split the edge from (280, 180).");
            Assert.That(editor.Figure.Segments[1].GetEndPoint().Value, Is.EqualTo(new BtlPoint(180, 140)));
            Assert.That(editor.Figure.Segments[2].GetEndPoint().Value, Is.EqualTo(new BtlPoint(80, 100)));
        });
    }

    // On an open figure with an implicit start, the first segment that loaded is only the start point, so a bend there
    // bends the edge leaving it and nothing leading into it.
    [AvaloniaTest]
    public async Task A_bend_at_the_start_of_an_open_figure_past_segments_that_did_not_load_bends_only_the_edge_leaving_it()
    {
        using var editor = await Fixture.Create();
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.Segments.Insert(0, new FallbackPathSegment());
        }
        HeadlessTestHelpers.Render(3);

        editor.Key(Key.B);
        editor.Click(80, 100);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Figure.Segments[1], Is.TypeOf<LineSegment>(), "No edge leads into the start.");
            Assert.That(editor.Figure.Segments[2], Is.TypeOf<CubicBezierSegment>(), "The edge leaving the start bends.");
            Assert.That(editor.Figure.Segments.OfType<CubicBezierSegment>()
                    .SelectMany(cubic => new[] { cubic.ControlPoint1.Value, cubic.ControlPoint2.Value }),
                Has.None.Matches<BtlPoint>(handle => handle.IsInvalid));
        });
    }

    // With an implicit start and only segments that did not load there is no point to draw from, so a segment added
    // from the canvas menu gets its handles on its own point.
    [AvaloniaTest]
    [TestCase("Cubic")]
    [TestCase("Quad")]
    [TestCase("Conic")]
    public async Task A_segment_the_menu_adds_past_segments_that_did_not_load_gets_no_NaN_handles(string tag)
    {
        using var editor = await Fixture.Create(empty: true);
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.Segments.Add(new FallbackPathSegment());
        }
        HeadlessTestHelpers.Render(3);
        PathSegment? added = null;
        Mock.Get(editor.Model.FigureContext.Value!)
            .Setup(context => context.AddSegment(It.IsAny<PathSegment>()))
            .Callback<PathSegment>(segment => added = segment);

        editor.MouseDown(new Point(200, 150), MouseButton.Right);
        editor.MouseUp(new Point(200, 150), MouseButton.Right);
        var menu = (FAMenuFlyout)editor.View.FindControl<Canvas>("canvas")!.ContextFlyout!;
        menu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Tag, tag))
            .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));

        Assert.That(added, Is.Not.Null, "The menu added no segment.");
        Assert.That(PathEditorHelper.GetControlPointProperties(added!).Select(property => property.Value),
            Has.All.Matches<BtlPoint>(handle => MathF.Abs(handle.X - 200) < 0.01f && MathF.Abs(handle.Y - 150) < 0.01f));
    }

    private sealed class Fixture : IDisposable
    {
        public required EditViewModel Editor { get; init; }
        public required PathEditorTabViewModel Model { get; init; }
        public required PathFigure Figure { get; init; }
        public required PathEditorTabView View { get; init; }
        public required Window Window { get; init; }
        private readonly List<IDisposable> _properties = [];
        private PathEditorView? _overlay;
        public PathSegment[] Selected => View.GetSelectedAnchors().Select(t => (PathSegment)t.DataContext!).ToArray();

        public static async Task<Fixture> Create(bool empty = false, PathFigure? pathFigure = null)
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("path-edit");
            var figure = pathFigure ?? new PathFigure();
            if (!empty && pathFigure == null)
            {
                figure.Segments.Add(new LineSegment(80, 100));
                figure.Segments.Add(new LineSegment(180, 100));
                figure.Segments.Add(new LineSegment(280, 180));
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            var shape = new GeometryShape { Data = { Value = geometry } };
            var element = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(10) } };
            element.AddObject(shape);
            scene.Children.Add(element);
            editor.HistoryManager.Commit();
            editor.HistoryManager.Clear();
            var parent = new Mock<IGeometryEditorContext>();
            var geometryValue = Observable.Return<Beutl.Media.Geometry?>(geometry).Concat(Observable.Never<Beutl.Media.Geometry?>()).ToReadOnlyReactiveProperty();
            parent.SetupGet(p => p.Value).Returns(geometryValue);
            parent.Setup(p => p.GetService(typeof(Element))).Returns(element);
            var context = new Mock<IPathFigureEditorContext>();
            var figureValue = new ReadOnlyReactiveProperty<PathFigure>(Observable.Return(figure).Concat(Observable.Never<PathFigure>()), initialValue: figure);
            context.SetupGet(p => p.Value).Returns(figureValue);
            context.Setup(p => p.GetParentContext()).Returns(parent.Object);
            var model = new PathEditorTabViewModel(editor);
            model.FigureContext.Value = context.Object;
            var view = new PathEditorTabView { DataContext = model };
            var window = new Window
            {
                Content = view,
                Width = 640,
                Height = 420,
                RequestedThemeVariant = ThemeVariant.Dark
            };
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(model.FigureContext.Value, Is.SameAs(context.Object), "figure context");
            Assert.That(model.PathFigure.Value, Is.SameAs(figure), "path figure");
            Assert.That(model.PathGeometry.Value, Is.SameAs(geometry), "geometry");
            Assert.That(view.FindControl<Canvas>("canvas")!.Children.OfType<Thumb>().Count(), Is.GreaterThanOrEqualTo(figure.Segments.Count), "anchors");
            view.Focus();
            var result = new Fixture { Editor = editor, Model = model, Figure = figure, View = view, Window = window };
            result._properties.Add(geometryValue);
            result._properties.Add(figureValue);
            return result;
        }

        public static async Task<Fixture> CreateCollapsed(bool last)
        {
            var fixture = await Create(empty: true);
            var first = PathEditingOperations.Cubic(new(170, 90), new(203.16f, -83.08f), new(203.16f, -83.08f));
            var second = PathEditingOperations.Cubic(first.EndPoint.Value, new(440, -90), new(430, -20));
            var third = PathEditingOperations.Cubic(new(430, 85), new(310, 155), new(250, 140));
            using (fixture.Editor.HistoryManager.SuppressRecording())
            {
                fixture.Figure.IsClosed.Value = true;
                fixture.Figure.Segments.Add(first);
                fixture.Figure.Segments.Add(second);
                fixture.Figure.Segments.Add(third);
                if (last)
                {
                    third.ControlPoint2.Value = third.EndPoint.Value;
                    first.ControlPoint1.Value = third.EndPoint.Value;
                }
            }
            fixture.View.Matrix = Matrix.CreateTranslation(-120, 140);
            HeadlessTestHelpers.Render(3);
            BtlPoint selected = last ? third.EndPoint.Value : first.EndPoint.Value;
            fixture.Click(selected.X - 120, selected.Y + 140);
            HeadlessTestHelpers.Render(3);
            return fixture;
        }

        // Shows the player's path editor for the figure in place of the tab. Dispose takes it down.
        public PathEditorView UsePreviewOverlay()
        {
            Editor.Player.PathEditor.FigureContext.Value = Model.FigureContext.Value;
            _overlay = new PathEditorView { DataContext = Editor.Player.PathEditor };
            Window.Content = _overlay;
            HeadlessTestHelpers.Render(3);
            return _overlay;
        }

        // The anchor thumb of segment in view, the tab or the preview overlay.
        public static Thumb FindAnchor(IPathEditorView view, PathSegment segment)
            => ((Control)view).FindControl<Canvas>("canvas")!.Children.OfType<Thumb>()
                .Single(t => ReferenceEquals(t.DataContext, segment) && !t.Classes.Contains("control"));

        private Point WindowPoint(Point point) => ((Control)Window.Content!).FindControl<Canvas>("canvas")!.TranslatePoint(point, Window)!.Value;

        public void MouseDown(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseDown(WindowPoint(point), button, modifiers);

        public void MouseUp(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseUp(WindowPoint(point), button, modifiers);

        public void MouseMove(Point point, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseMove(WindowPoint(point), modifiers);

        public void MouseWheel(Point point, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseWheel(WindowPoint(point), delta, modifiers);

        public void ClickControl(Control control, RawInputModifiers modifiers = RawInputModifiers.None, Action? beforePress = null)
        {
            control.BringIntoView();
            HeadlessTestHelpers.Render(2);
            var top = TopLevel.GetTopLevel(control)!;
            var hitTarget = control.IsHitTestVisible ? control
                : control.GetVisualAncestors().OfType<Control>().First(ancestor => ancestor.IsHitTestVisible);
            Point point = ReadyPoint();
            beforePress?.Invoke();
            top.MouseDown(point, MouseButton.Left, modifiers);
            top.MouseUp(point, MouseButton.Left, modifiers);
            HeadlessTestHelpers.Render(3);

            Point ReadyPoint()
            {
                // MouseMove pumps dispatcher/render work. A preview matrix or hover layout
                // can move the target in that work, and compositor hit data may lag layout.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    HeadlessTestHelpers.Render(2);
                    Point before = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                    top.MouseMove(before, modifiers);
                    Point after = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                    var hit = top.InputHitTest(after) as Visual;
                    if (before == after && (!control.IsEffectivelyEnabled || ReferenceEquals(hit, hitTarget)
                        || hit?.GetVisualAncestors().Contains(hitTarget) == true)) return after;
                }
                throw new AssertionException($"The rendered hit target did not settle on {control.GetType().Name} ({control.Name}).");
            }
        }

        public void Click(double x, double y, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Point point = new(x, y);
            MouseMove(point);
            MouseDown(point, MouseButton.Left, modifiers);
            MouseUp(point, MouseButton.Left, modifiers);
            HeadlessTestHelpers.Render(2);
        }

        public void Drag(Point start, Point end, RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
        {
            MouseMove(start);
            MouseDown(start, button, modifiers);
            MouseMove(end, modifiers | (button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.MiddleMouseButton));
            HeadlessTestHelpers.Render();
            MouseUp(end, button, modifiers);
            HeadlessTestHelpers.Render(2);
        }

        public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            View.Focus();
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Render(2);
        }

        public void Dispose()
        {
            if (_overlay != null)
            {
                _overlay.DataContext = null;
                Editor.Player.PathEditor.FigureContext.Value = null;
            }
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
            foreach (var property in _properties) property.Dispose();
        }
    }
}

using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
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
using Beutl.ViewModels.Editors;
using Beutl.Views;
using Beutl.Views.Editors;
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(90, 100)));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(190, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(280, 180)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        Assert.That(editor.Editor.HistoryManager.Undo(), Is.True);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
        editor.Key(Key.Escape);
        editor.Key(Key.Right);
        Assert.That(editor.Selected, Is.Empty);
        Assert.That(editor.Figure.Segments[1].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(180, 100)));
        editor.Drag(new(60, 75), new(110, 125));
        editor.Drag(new(260, 160), new(300, 200), RawInputModifiers.Shift);
        Assert.That(editor.Selected, Is.EqualTo(new[] { editor.Figure.Segments[0], editor.Figure.Segments[2] }));
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control, false)]
    [TestCase(RawInputModifiers.Meta, false)]
    [TestCase(RawInputModifiers.Control, true)]
    [TestCase(RawInputModifiers.Meta, true)]
    public async Task Control_and_command_click_toggle_points_in_the_selection(RawInputModifiers modifier, bool preview)
    {
        using var editor = await Fixture.Create();
        IPathEditorView view = editor.View;
        var previewModel = editor.Editor.Player.PathEditor;
        PathEditorView? overlay = null;
        if (preview)
        {
            previewModel.FigureContext.Value = editor.Model.FigureContext.Value;
            overlay = new PathEditorView { DataContext = previewModel };
            editor.Window.Content = overlay;
            view = overlay;
            HeadlessTestHelpers.Render(3);
        }
        try
        {
            ClickAnchor(0);
            ClickAnchor(1, modifier);
            Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[0], editor.Figure.Segments[1] }));
            ClickAnchor(0, modifier);
            Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[1] }));
            ClickAnchor(2);
            Assert.That(Selected(), Is.EqualTo(new[] { editor.Figure.Segments[2] }));
            Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        }
        finally
        {
            if (overlay != null)
            {
                overlay.DataContext = null;
                previewModel.FigureContext.Value = null;
            }
        }

        PathSegment[] Selected() => view.GetSelectedAnchors().Select(t => (PathSegment)t.DataContext!).ToArray();

        void ClickAnchor(int index, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var anchor = editor.Figure.Segments[index];
            var thumb = ((Control)view).FindControl<Canvas>("canvas")!.Children.OfType<Thumb>()
                .Single(t => ReferenceEquals(t.DataContext, anchor) && !t.Classes.Contains("control"));
            Point point = PathEditorHelper.GetCanvasPosition(thumb);
            editor.Click(point.X, point.Y, modifiers);
        }
    }

    [AvaloniaTest]
    public async Task Drag_selected_points_constrains_axis_and_undo_restores_the_group()
    {
        using var editor = await Fixture.Create();
        editor.Click(80, 100);
        editor.Click(180, 100, RawInputModifiers.Shift);
        editor.Drag(new(80, 100), new(120, 117), RawInputModifiers.Shift);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(120, 100)));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(220, 100)));
        Assert.That(editor.Selected, Has.Length.EqualTo(2));
        Assert.That(editor.View.SkipUpdatePosition, Is.False);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
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
        Assert.That(curve.ControlPoint2.CurrentValue, Is.EqualTo(new BtlPoint(145, 160)));
        Assert.That(((CubicBezierSegment)editor.Figure.Segments[2]).ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(215, 160)));
        editor.Click(80, 100);
        Assert.That(editor.Figure.IsClosed.CurrentValue, Is.True);
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
    public async Task Pen_closing_existing_cubic_applies_outgoing_and_preserves_incoming(bool animatedIncoming)
    {
        var figure = new PathFigure();
        var first = PathEditingOperations.Cubic(new(30, 90), new(40, 140), new(80, 100));
        if (animatedIncoming)
        {
            var animation = new KeyFrameAnimation<BtlPoint>();
            animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = new(35, 85) });
            first.ControlPoint2.Animation = animation;
        }
        figure.Segments.Add(first);
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        editor.Key(Key.P);
        editor.Click(280, 180);
        editor.Drag(new(240, 120), new(270, 120));
        var before = CoreSerializer.SerializeToJsonObject(figure);
        var incoming = first.ControlPoint2.Animation;
        int history = editor.Editor.HistoryManager.UndoCount;
        editor.Click(80, 100);
        Assert.That(figure.IsClosed.CurrentValue, Is.True);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Move));
        Assert.That(figure.Segments[0], Is.SameAs(first));
        Assert.That(first.ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(270, 120)));
        Assert.That(first.ControlPoint2.CurrentValue, Is.EqualTo(new BtlPoint(40, 140)));
        Assert.That(first.ControlPoint2.Animation, Is.SameAs(incoming));
        Assert.That(first.EndPoint.CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
        var after = CoreSerializer.SerializeToJsonObject(figure);
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"implicit-cubic-close-{animatedIncoming}.png"), PngBitmapEncoderOptions.Default);
        }
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
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = property.CurrentValue });
                property.Animation = animation;
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
        editor.Click(220, 160);
        Assert.That(((CubicBezierSegment)figure.Segments[^1]).ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(270, 120)));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Pen_closes_at_explicit_start_without_changing_the_first_edge(bool curved, bool preview)
    {
        var figure = new PathFigure { StartPoint = { CurrentValue = new(80, 100) } };
        var first = new LineSegment(180, 100);
        figure.Segments.Add(first);
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        IPathEditorView view = editor.View;
        PathEditorView? overlay = null;
        var model = editor.Editor.Player.PathEditor;
        if (preview)
        {
            model.FigureContext.Value = editor.Model.FigureContext.Value;
            overlay = new PathEditorView { DataContext = model };
            editor.Window.Content = overlay;
            view = overlay;
            HeadlessTestHelpers.Render(3);
            overlay.Tool = PathEditorTool.Pen;
        }
        else editor.Key(Key.P);
        try
        {
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
            Assert.That(figure.IsClosed.CurrentValue, Is.False, "the first segment endpoint is not the start");
            Assert.That(figure.Segments, Is.EqualTo(originalEdges));
            Assert.That(JsonNode.DeepEquals(originalFirst, CoreSerializer.SerializeToJsonObject(first)), Is.True);
            Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history));
            Capture("open");

            var startMarker = ((Control)view).FindControl<Canvas>("canvas")!.Children.OfType<Border>()
                .Single(b => b.Name == "PathStartPoint");
            editor.ClickControl(startMarker);
            Assert.That(figure.IsClosed.CurrentValue, Is.True);
            Assert.That(figure.Segments.Take(originalEdges.Length), Is.EqualTo(originalEdges));
            Assert.That(JsonNode.DeepEquals(originalFirst, CoreSerializer.SerializeToJsonObject(first)), Is.True);
            Assert.That(figure.Segments, Has.Count.EqualTo(originalEdges.Length + (curved ? 1 : 0)));
            if (curved)
            {
                var closing = (CubicBezierSegment)figure.Segments[^1];
                Assert.That(closing.EndPoint.CurrentValue, Is.EqualTo(figure.StartPoint.CurrentValue));
                Assert.That(closing.ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(270, 120)));
            }
            Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
            Capture("closed");
            editor.Editor.HistoryManager.Undo();
            Assert.That(figure.IsClosed.CurrentValue, Is.False);
            Assert.That(figure.Segments, Is.EqualTo(originalEdges));
        }
        finally
        {
            if (overlay != null) { overlay.DataContext = null; model.FigureContext.Value = null; }
        }

        Point Screen(BtlPoint p) => view.Matrix.Transform(new Point(p.X, p.Y)) * view.Scale;
        void Click(BtlPoint p) { Point screen = Screen(p); editor.Click(screen.X, screen.Y); }
        void ClickAnchor(PathSegment segment)
        {
            var thumb = ((Control)view).FindControl<Canvas>("canvas")!.Children.OfType<Thumb>()
                .Single(t => ReferenceEquals(t.DataContext, segment) && !t.Classes.Contains("control"));
            editor.ClickControl(thumb);
        }
        void Capture(string stage)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            HeadlessTestHelpers.Render(3);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"explicit-start-{curved}-{preview}-{stage}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Pen_rejects_closure_when_closed_state_has_animation_or_expression(bool explicitStart, bool expression)
    {
        var figure = new PathFigure();
        if (explicitStart) figure.StartPoint.CurrentValue = new(80, 100);
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
                animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.Zero, Value = false });
                animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(1), Value = true });
                figure.IsClosed.Animation = animation;
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
        Assert.That(((CubicBezierSegment)figure.Segments[^1]).ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(270, 120)),
            "a rejected closure must preserve the pending outgoing handle for continuation");
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Pen_retains_pending_handle_when_closing_endpoint_is_not_static(bool last, bool expression)
    {
        var figure = new PathFigure { StartPoint = { CurrentValue = new(80, 100) } };
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
                animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = property.CurrentValue });
                property.Animation = animation;
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
            property.Animation = null;
            property.Expression = null;
        }
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        editor.Click(80, 100);
        Assert.That(figure.IsClosed.CurrentValue, Is.True);
        var closing = (CubicBezierSegment)figure.Segments[^1];
        Assert.That(closing.EndPoint.CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(closing.ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(270, 120)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(history + 1));
    }

    [AvaloniaTest]
    public async Task Explicit_start_marker_requires_a_live_pen_continuation()
    {
        var figure = new PathFigure { StartPoint = { CurrentValue = new(80, 100) } };
        figure.Segments.Add(new LineSegment(180, 100));
        figure.Segments.Add(new LineSegment(280, 180));
        using var editor = await Fixture.Create(pathFigure: figure);
        var marker = editor.View.FindControl<Canvas>("canvas")!.Children.OfType<Border>()
            .Single(b => b.Name == "PathStartPoint");
        editor.Key(Key.P);
        Assert.That(marker.IsVisible, Is.False, "selecting Pen alone cannot offer closure");
        Capture("inactive");
        editor.Click(280, 180);
        Assert.That(marker.IsVisible, Is.True);
        Capture("active");
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

        void Capture(string state)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            HeadlessTestHelpers.Render(3);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"close-marker-{state}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Player_path_toolbar_stays_in_the_viewport_when_the_frame_is_transformed(bool light)
    {
        using var editor = await Fixture.Create(light: light);
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
            if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = editor.Window.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"player-toolbar-transformed-{light}.png"), PngBitmapEncoderOptions.Default);
            }
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
            editor.Figure.StartPoint.CurrentValue = new(10000, 10000);
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
        TestContext.WriteLine($"Control-point evaluations on distant hover: {evaluations}");
        Assert.That(evaluations, Is.LessThanOrEqualTo(count * 4), "far edges must not be adaptively evaluated");
    }

    [AvaloniaTest]
    public async Task Pen_inserts_on_edge_without_changing_existing_shape()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(4));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(130, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(180, 100)));
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
            editor.Figure.StartPoint.CurrentValue = new(80, 100);
            switch (kind)
            {
                case "arc":
                    editor.Figure.Segments.Add(new ArcSegment
                    {
                        Radius = { CurrentValue = new(100, 80) },
                        Point = { CurrentValue = new(280, 100) }
                    });
                    click = new(180, 20);
                    break;
                case "closing-edge":
                    editor.Figure.IsClosed.CurrentValue = true;
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
                    animation.KeyFrames.Add(new KeyFrame<BtlPoint> { Value = new(280, 260) });
                    animated.GetEndPoint().Animation = animation;
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
        BtlPoint point = inserted.GetEndPoint().CurrentValue;
        Assert.That(point.X, Is.EqualTo(click.X).Within(.1));
        Assert.That(point.Y, Is.EqualTo(click.Y).Within(.1));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"insert-{kind}.png"), PngBitmapEncoderOptions.Default);
        }
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
            editor.Figure.StartPoint.CurrentValue = start;
            editor.Figure.Segments.Add(curve);
        }
        HeadlessTestHelpers.Render(3);
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(2));
        Assert.That(editor.Figure.Segments[1], Is.SameAs(curve));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue.X, Is.EqualTo(130).Within(.5));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue.Y, Is.EqualTo(100).Within(.001));
    }

    [AvaloniaTest]
    public async Task Pen_inserts_on_a_zoomed_bezier_between_hit_test_samples()
    {
        using var editor = await Fixture.Create(empty: true);
        var curve = PathEditingOperations.Cubic(new(0, 2000), new(2000, 2000), new(2000, 0));
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            editor.Figure.StartPoint.CurrentValue = default;
            editor.Figure.Segments.Add(curve);
        }
        BtlPoint point = PathEditingOperations.Evaluate(default, curve, 32.5f / 64, CompositionContext.Default);
        editor.View.Matrix = Matrix.CreateScale(64, 64) * Matrix.CreateTranslation(180 - point.X * 64, 100 - point.Y * 64);
        HeadlessTestHelpers.Render(3);
        editor.Key(Key.P);
        editor.Click(180, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(2));
        Assert.That(editor.Figure.Segments[1], Is.SameAs(curve), "split the curve instead of extending the path");
        var inserted = editor.Figure.Segments[0].GetEndPoint().CurrentValue;
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
        Assert.That(incoming.ControlPoint2.CurrentValue, Is.Not.EqualTo(new BtlPoint(180, 100)));
        BtlPoint original = outgoing.ControlPoint1.CurrentValue;
        BtlPoint point = incoming.ControlPoint2.CurrentValue;
        editor.Key(Key.V);
        editor.Drag(new(point.X, point.Y), new(point.X, point.Y - 30), RawInputModifiers.Alt);
        Assert.That(outgoing.ControlPoint1.CurrentValue, Is.EqualTo(original));
        editor.Drag(new(point.X, point.Y - 30), new(point.X - 15, point.Y - 35));
        Assert.That(outgoing.ControlPoint1.CurrentValue, Is.Not.EqualTo(original));
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
        Point point = PathEditorHelper.GetCanvasPosition(thumb);
        editor.MouseDown(point, MouseButton.Left);
        editor.MouseUp(point, MouseButton.Left);
        Assert.That(overlay.GetSelectedAnchors(), Has.Length.EqualTo(1));
        editor.Window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        editor.Window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(90, 100)));
        editor.MouseDown(new(350, 280), MouseButton.Middle);
        editor.MouseMove(new(370, 300), RawInputModifiers.MiddleMouseButton);
        editor.MouseUp(new(370, 300), MouseButton.Middle);
        Assert.That(pan, Is.EqualTo(new Vector(20, 20)));
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                editor.Window.RequestedThemeVariant = theme;
                editor.Window.Background = Avalonia.Media.Brushes.Gray;
                editor.MouseMove(new(10, 10));
                HeadlessTestHelpers.Render(3);
                using var frame = editor.Window.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"preview-toolbar-{theme}.png"), PngBitmapEncoderOptions.Default);
            }
        }
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
        Assert.That(editor.Figure.Segments[2].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(281, 180)));
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(95, 110)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task Animated_nudge_changes_the_current_keyframe_and_undo_restores_it()
    {
        using var editor = await Fixture.Create();
        var first = new KeyFrame<BtlPoint> { KeyTime = TimeSpan.Zero, Value = new(80, 100) };
        var second = new KeyFrame<BtlPoint> { KeyTime = TimeSpan.FromSeconds(1), Value = new(110, 130) };
        var animation = new KeyFrameAnimation<BtlPoint> { UseGlobalClock = true };
        animation.KeyFrames.Add(first);
        animation.KeyFrames.Add(second);
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.Segments[0].GetEndPoint().Animation = animation;
        HeadlessTestHelpers.Render(3);
        editor.Click(80, 100);
        editor.Key(Key.Right, RawInputModifiers.Shift);
        Assert.That(first.Value, Is.EqualTo(new BtlPoint(90, 100)));
        Assert.That(second.Value, Is.EqualTo(new BtlPoint(110, 130)));
        Assert.That(editor.Figure.Segments[0].GetEndPoint().Animation, Is.SameAs(animation));
        editor.Editor.HistoryManager.Undo();
        Assert.That(first.Value, Is.EqualTo(new BtlPoint(80, 100)));
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Single_keyframe_control_allows_insertion_on_each_segment(int index)
    {
        var figure = new PathFigure { IsClosed = { CurrentValue = true } };
        figure.Segments.Add(PathEditingOperations.Cubic(new(589.81f, 56.57f), new(392.78f, 134.8f), new(276.76f, 115.35f)));
        figure.Segments.Add(PathEditingOperations.Cubic(new(191.89f, 101.12f), new(130.91277f, -95.69183f), new(208.2f, -133.51f)));
        var third = PathEditingOperations.Cubic(new(323.6624f, -190.00803f), new(437.95f, -115.68f), new(580.59f, -33.32f));
        var singleKey = new KeyFrameAnimation<BtlPoint>();
        singleKey.KeyFrames.Add(new KeyFrame<BtlPoint>
        {
            Value = new(571.368f, -123.206566f),
            KeyTime = TimeSpan.Zero,
            Easing = new SplineEasing(0, 0, 1, 1)
        });
        third.ControlPoint2.Animation = singleKey;
        figure.Segments.Add(third);
        // Optional local reproduction input; the saved project is read only and is never edited by this test.
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_REPRO") is { Length: > 0 } source)
        {
            var json = JsonNode.Parse(File.ReadAllText(source))!;
            figure = (PathFigure)CoreSerializer.DeserializeFromJsonObject(
                json["Objects"]![0]!["Data"]!["Figures"]![0]!.AsObject(), typeof(PathFigure));
        }
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
        var animation = ((CubicBezierSegment)figure.Segments[2]).ControlPoint2.Animation!;
        var key = (KeyFrame<BtlPoint>)((KeyFrameAnimation<BtlPoint>)animation).KeyFrames.Single();
        var keyId = key.Id;
        var easing = key.Easing;
        var before = CoreSerializer.SerializeToJsonObject(figure);
        Capture("before");
        Point click = editor.View.Matrix.Transform(new Point(expected[50].X, expected[50].Y));
        editor.Key(Key.P);
        editor.Click(click.X, click.Y);
        Assert.That(figure.Segments, Has.Count.EqualTo(4), "the keyed segment must not silently reject insertion");
        var inserted = figure.Segments[index];
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(inserted));
        Assert.That(figure.Segments[index + 1], Is.SameAs(original));
        if (index == 2) Assert.That(original.ControlPoint2.Animation, Is.SameAs(animation));
        Assert.That(key.Id, Is.EqualTo(keyId));
        Assert.That(key.Easing, Is.SameAs(easing));
        Assert.That(key.KeyTime, Is.EqualTo(TimeSpan.Zero));
        var animatedProperties = figure.Segments.SelectMany(s => PathEditorHelper.GetControlPointProperties(s).Append(s.GetEndPoint()))
            .Select(p => p.Animation).OfType<KeyFrameAnimation<BtlPoint>>().ToArray();
        Assert.That(animatedProperties.Select(a => a.Id).Distinct().Count(), Is.EqualTo(animatedProperties.Length));
        Assert.That(animatedProperties.SelectMany(a => a.KeyFrames).Select(k => k.Id).Distinct().Count(), Is.EqualTo(animatedProperties.Length));
        if (index == 2)
        {
            Assert.That(animatedProperties, Has.Length.EqualTo(4));
            var derived = ((KeyFrameAnimation<BtlPoint>)inserted.GetEndPoint().Animation!).KeyFrames[0];
            Assert.That(derived.Easing, Is.Not.SameAs(easing));
            Assert.That(derived.KeyTime, Is.EqualTo(key.KeyTime));
        }
        var leftBase = BaseCurve((CubicBezierSegment)inserted);
        var rightBase = BaseCurve(original);
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            var actual = t <= .5f ? PathEditingOperations.Evaluate(start, leftBase, t * 2, context)
                : PathEditingOperations.Evaluate(leftBase.EndPoint.CurrentValue, rightBase, (t - .5f) * 2, context);
            Assert.That(((Beutl.Graphics.Vector)(actual - expectedBase[i])).Length, Is.LessThan(.02), "base values must not be replaced with keyframe values");
        }
        foreach (var time in new[] { -1, 0, 1, 5 })
        {
            context = new CompositionContext(TimeSpan.FromSeconds(time));
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                var actual = t <= .5f ? PathEditingOperations.Evaluate(start, inserted, t * 2, context)
                    : PathEditingOperations.Evaluate(inserted.GetEndPoint().GetValue(context), original, (t - .5f) * 2, context);
                Assert.That(((Beutl.Graphics.Vector)(actual - expected[i])).Length, Is.LessThan(.02));
            }
        }
        Capture("after");
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
            curve.ControlPoint1.CurrentValue, curve.ControlPoint2.CurrentValue, curve.EndPoint.CurrentValue);

        void Capture(string stage)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            HeadlessTestHelpers.Render(3);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"single-key-{index}-{stage}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTest]
    public async Task Pen_on_a_time_varying_edge_does_not_append_an_unrelated_point()
    {
        using var editor = await Fixture.Create();
        var animation = new KeyFrameAnimation<BtlPoint>();
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = TimeSpan.Zero, Value = new(180, 100) });
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = TimeSpan.FromSeconds(1), Value = new(200, 120) });
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.Segments[1].GetEndPoint().Animation = animation;
        editor.Key(Key.P);
        editor.Click(130, 100);
        Assert.That(editor.Figure.Segments, Has.Count.EqualTo(3));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().Animation, Is.SameAs(animation));
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue, Is.EqualTo(new BtlPoint(80, 100)));
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
        Assert.That(editor.Figure.Segments[0].GetEndPoint().CurrentValue.X, Is.EqualTo(100));
        Assert.That(editor.Figure.Segments[1].GetEndPoint().CurrentValue.X, Is.EqualTo(200));
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
        var middle = PathEditingOperations.Evaluate(new BtlPoint(80, 100), segment, .5f, new CompositionContext(TimeSpan.Zero));
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
        Assert.That(controls.Select(c => ((BaseEditorViewModel)c.DataContext!).PropertyAdapter.GetEngineProperty()),
            Is.EqualTo(new Beutl.Engine.IProperty[] { curve.EndPoint, curve.ControlPoint2, next.ControlPoint1 }));
        Assert.That(controls.Select(c => c.Header), Is.EqualTo(new[]
        {
            GraphicsStrings.Position, Strings.PathEditor_IncomingControlPoint, Strings.PathEditor_OutgoingControlPoint
        }));
        Assert.That(controls, Has.Length.EqualTo(3));
        foreach (var control in controls)
        {
            Assert.That(control.EditorStyle, Is.EqualTo(PropertyEditorStyle.Normal));
            Assert.That(control.MenuContent, Is.TypeOf<PropertyEditorMenu>());
            Assert.That(control.Margin, Is.EqualTo(new Thickness(4, 0)));
            Assert.That(((BaseEditorViewModel)control.DataContext!).GetService(typeof(Element)),
                Is.SameAs(editor.Model.Element.Value));
        }
        Assert.That(editor.View.FindControl<Border>("PointProperties")!.Padding.Left, Is.Zero);
        Assert.That(editor.View.FindControl<Vector2Editor<float>>("PointPosition")!.IsVisible, Is.False);

        var controlPoint = controls.Single(c => ReferenceEquals(
            ((BaseEditorViewModel)c.DataContext!).PropertyAdapter.GetEngineProperty(), curve.ControlPoint2));
        var context = (BaseEditorViewModel)controlPoint.DataContext!;
        var input = controlPoint.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        int undo = editor.Editor.HistoryManager.UndoCount;
        BtlPoint previous = curve.ControlPoint2.CurrentValue;
        input.BringIntoView();
        HeadlessTestHelpers.Render(2);
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("115");
        editor.View.Focus();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.CurrentValue, Is.EqualTo(new BtlPoint(115, previous.Y)));
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
        Assert.That(curve.ControlPoint2.CurrentValue, Is.EqualTo(default(BtlPoint)));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(undo + 2));
        editor.Editor.HistoryManager.Undo();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.CurrentValue, Is.EqualTo(new BtlPoint(115, previous.Y)));

        context.PrepareToEditAnimation();
        HeadlessTestHelpers.Render(2);
        Assert.That(curve.ControlPoint2.Animation, Is.Not.Null);
        Assert.That(controlPoint.KeyFrameCount, Is.GreaterThan(0));
        Assert.That(controlPoint.MenuContent, Is.SameAs(menu));
        editor.Click(80, 100);
        Assert.That(context.IsDisposed, Is.True);
        Assert.That(((BaseEditorViewModel)editor.Model.PointProperties.Value[0]).PropertyAdapter.GetEngineProperty(),
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
        BtlPoint point = outgoing.ControlPoint1.CurrentValue;
        editor.Click(point.X, point.Y);
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(anchor));
        Assert.That(editor.Model.PointProperties.Value, Is.SameAs(properties));
        Assert.That(((BaseEditorViewModel)properties[0]).PropertyAdapter.GetEngineProperty(), Is.SameAs(anchor.GetEndPoint()));
        Assert.That(((BaseEditorViewModel)properties[2]).PropertyAdapter.GetEngineProperty(), Is.SameAs(outgoing.ControlPoint1));
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
        BtlPoint unrelated = anchor.ControlPoint1.CurrentValue;
        BtlPoint otherHandle = next.ControlPoint2.CurrentValue;
        BtlPoint previous = next.ControlPoint1.CurrentValue;
        var control = editor.View.FindControl<ItemsControl>("PointPropertyList")!
            .GetVisualDescendants().OfType<Vector2Editor<float>>().Last();
        var input = control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_InnerFirstTextBox");
        input.BringIntoView();
        input.Focus();
        input.SelectAll();
        editor.Window.KeyTextInput("220");
        editor.View.Focus();
        HeadlessTestHelpers.Render(3);
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(new BtlPoint(220, previous.Y)));
        Assert.That(anchor.ControlPoint1.CurrentValue, Is.EqualTo(unrelated));
        Assert.That(next.ControlPoint2.CurrentValue, Is.EqualTo(otherHandle));
        Assert.That(anchor.EndPoint.CurrentValue, Is.EqualTo(new BtlPoint(180, 100)));
        Assert.That(next.EndPoint.CurrentValue, Is.EqualTo(new BtlPoint(280, 180)));
        editor.Editor.HistoryManager.Undo();
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(previous));
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
        var point = next.ControlPoint1.CurrentValue;
        editor.Click(point.X, point.Y);
        Assert.That(editor.View.GetSelectedAnchors(), Has.Length.EqualTo(2));
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(editor.Figure.Segments[0]));
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(((BaseEditorViewModel)editor.Model.PointProperties.Value[1]).PropertyAdapter.GetEngineProperty(),
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
        var outgoingEditor = (BaseEditorViewModel)editor.Model.PointProperties.Value[2];
        editor.Editor.HistoryManager.ExecuteInTransaction(() =>
            editor.Figure.Segments[2] = new LineSegment(280, 180));
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(anchor));
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(outgoingEditor.IsDisposed, Is.True);
        editor.Editor.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(3));
        Assert.That(((BaseEditorViewModel)editor.Model.PointProperties.Value[2]).PropertyAdapter.GetEngineProperty(),
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
        figure.IsClosed.CurrentValue = true;
        Check(first, first.EndPoint, first.ControlPoint2, middle.ControlPoint1);
        Check(last, last.EndPoint, last.ControlPoint2, first.ControlPoint1);
        figure.StartPoint.CurrentValue = new(-10, -10);
        Check(last, last.EndPoint, last.ControlPoint2);
        figure.IsClosed.CurrentValue = false;
        Check(first, first.EndPoint, first.ControlPoint2, middle.ControlPoint1);

        void Check(PathSegment anchor, params Beutl.Engine.IProperty<BtlPoint>[] expected)
        {
            var actual = PathPointProperties.Get(figure, anchor, new CompositionContext(TimeSpan.Zero));
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
        figure.IsClosed.CurrentValue = true;
        var properties = PathPointProperties.Get(figure, segment, new CompositionContext(TimeSpan.Zero));
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
        BtlPoint point = anchor.EndPoint.CurrentValue;
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
        BtlPoint bentIncoming = anchor.ControlPoint2.CurrentValue;
        BtlPoint bentOutgoing = next.ControlPoint1.CurrentValue;
        Assert.That(anchor.EndPoint.CurrentValue, Is.EqualTo(point));
        Assert.That(bentIncoming, Is.Not.EqualTo(point));
        Assert.That(bentOutgoing, Is.Not.EqualTo(point));
        Assert.That(incoming.IsVisible && outgoing.IsVisible, Is.True);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        Point from = PathEditorHelper.GetCanvasPosition(incoming);
        Point to = from + new Vector(-26, -15);
        Point expected = editor.View.Matrix.Invert().Transform(to);
        editor.Drag(from, to, independent ? RawInputModifiers.Alt : RawInputModifiers.None);
        Assert.That(anchor.EndPoint.CurrentValue, Is.EqualTo(point));
        Assert.That(anchor.ControlPoint2.CurrentValue.X, Is.EqualTo(expected.X).Within(.02));
        Assert.That(anchor.ControlPoint2.CurrentValue.Y, Is.EqualTo(expected.Y).Within(.02));
        Assert.That(next.ControlPoint1.CurrentValue, independent ? Is.EqualTo(bentOutgoing) : Is.Not.EqualTo(bentOutgoing));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(2));
        editor.Editor.HistoryManager.Undo();
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(bentIncoming));
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(bentOutgoing));
        editor.Editor.HistoryManager.Undo();
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(point));
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(point));
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
        BtlPoint point = anchor.EndPoint.CurrentValue;
        var context = new CompositionContext(TimeSpan.Zero);
        var animation = new KeyFrameAnimation<BtlPoint>();
        animation.KeyFrames.Add(new KeyFrame<BtlPoint> { KeyTime = TimeSpan.Zero, Value = point });
        var later = new KeyFrame<BtlPoint> { KeyTime = TimeSpan.FromSeconds(1), Value = new(220, -70) };
        animation.KeyFrames.Add(later);
        using (editor.Editor.HistoryManager.SuppressRecording())
        {
            if (scenario == "animated-anchor") anchor.EndPoint.Animation = animation;
            if (scenario == "animated-handles") anchor.ControlPoint2.Animation = animation;
            if (scenario == "coincident-neighbors")
                editor.Figure.Segments[^1].GetEndPoint().CurrentValue = next.EndPoint.CurrentValue;
            if (scenario == "nearly-collapsed")
                anchor.ControlPoint2.CurrentValue = point + new Beutl.Graphics.Vector(.001f, -.001f);
        }
        BtlPoint originalIncoming = anchor.ControlPoint2.GetValue(context);
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var tool = editor.View.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PathTools")
            .Children.OfType<RadioButton>().ElementAt(2);
        editor.ClickControl(tool);
        Assert.That(editor.View.Tool, Is.EqualTo(PathEditorTool.Bend));
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.GetValue(context), Is.Not.EqualTo(point), "incoming handle must expand");
        Assert.That(next.ControlPoint1.GetValue(context), Is.Not.EqualTo(point), "outgoing handle must expand");
        Assert.That(anchor.EndPoint.GetValue(context), Is.EqualTo(point));
        Assert.That(later.Value, Is.EqualTo(new BtlPoint(220, -70)), "a different keyframe must stay unchanged");
        foreach (var property in PathPointProperties.Get(editor.Figure, anchor, context).Skip(1))
        {
            var thumb = editor.View.FindThumb(property.Owner, property.Property)!;
            Assert.That(thumb.IsVisible, Is.True);
            Assert.That(((Vector)(PathEditorHelper.GetCanvasPosition(thumb) - position)).Length, Is.GreaterThan(8));
        }
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"bend-click-{scenario}.png"), PngBitmapEncoderOptions.Default);
        }
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(1));
        editor.Editor.HistoryManager.Undo();
        Assert.That(anchor.ControlPoint2.GetValue(context), Is.EqualTo(originalIncoming));
        Assert.That(next.ControlPoint1.GetValue(context), Is.EqualTo(point));
        if (scenario == "animated-anchor") Assert.That(anchor.EndPoint.Animation, Is.SameAs(animation));
        if (scenario == "animated-handles") Assert.That(anchor.ControlPoint2.Animation, Is.SameAs(animation));
    }

    [AvaloniaTest]
    public async Task Bend_at_an_explicit_closed_endpoint_toggles_only_its_own_handle()
    {
        using var editor = await Fixture.CreateCollapsed(last: true);
        using (editor.Editor.HistoryManager.SuppressRecording())
            editor.Figure.StartPoint.CurrentValue = new(190, 160);
        editor.View.Refresh();
        HeadlessTestHelpers.Render(3);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[^1];
        var first = (CubicBezierSegment)editor.Figure.Segments[0];
        BtlPoint unrelated = first.ControlPoint1.CurrentValue;
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.Not.EqualTo(anchor.EndPoint.CurrentValue));
        Assert.That(first.ControlPoint1.CurrentValue, Is.EqualTo(unrelated));
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(anchor.EndPoint.CurrentValue));
        Assert.That(first.ControlPoint1.CurrentValue, Is.EqualTo(unrelated));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task Selecting_a_corner_does_not_expand_it_and_escape_cancels_only_the_handle_drag()
    {
        using var editor = await Fixture.CreateCollapsed(false);
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        var next = (CubicBezierSegment)editor.Figure.Segments[1];
        BtlPoint original = anchor.EndPoint.CurrentValue;
        var thumb = editor.View.FindThumb(anchor, anchor.ControlPoint2)!;
        Point position = PathEditorHelper.GetCanvasPosition(thumb);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(original));
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(original));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        editor.Key(Key.V);
        BtlPoint incoming = anchor.ControlPoint2.CurrentValue;
        BtlPoint outgoing = next.ControlPoint1.CurrentValue;
        Point from = PathEditorHelper.GetCanvasPosition(thumb);
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(from + new Vector(-30, -10), RawInputModifiers.LeftMouseButton);
        editor.Key(Key.Escape);
        editor.MouseUp(from + new Vector(-30, -10), MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(incoming));
        Assert.That(next.ControlPoint1.CurrentValue, Is.EqualTo(outgoing));
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
        Assert.That(anchor.ControlPoint2.CurrentValue.X, Is.EqualTo(expected.X).Within(.02));
        Assert.That(anchor.ControlPoint2.CurrentValue.Y, Is.EqualTo(expected.Y).Within(.02));
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
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.EqualTo(anchor.EndPoint.CurrentValue));
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        overlay.Tool = PathEditorTool.Bend;
        editor.Click(position.X, position.Y);
        Assert.That(anchor.ControlPoint2.CurrentValue, Is.Not.EqualTo(anchor.EndPoint.CurrentValue));
        Assert.That(thumb.IsVisible, Is.True);
        overlay.DataContext = null;
        model.FigureContext.Value = null;
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Render_corner_selection_and_bend_without_artificial_handle_markers(bool light)
    {
        using var editor = await Fixture.CreateCollapsed(false, light);
        Assert.That(editor.Editor.HistoryManager.UndoCount, Is.Zero);
        Capture("corner-selection");
        var anchor = (CubicBezierSegment)editor.Figure.Segments[0];
        Point position = PathEditorHelper.GetCanvasPosition(editor.View.FindAnchorThumb(anchor)!);
        editor.Key(Key.B);
        editor.Click(position.X, position.Y);
        editor.Key(Key.V);
        Capture("bend-handles");

        void Capture(string stage)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                editor.MouseMove(new(10, 10));
                HeadlessTestHelpers.Render(3);
                using var frame = editor.Window.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"{stage}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
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
        Assert.That(point.X, Is.EqualTo(property.CurrentValue.X).Within(.51));
        Assert.That(point.Y, Is.EqualTo(property.CurrentValue.Y).Within(.51));
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            editor.MouseMove(new(10, 10));
            HeadlessTestHelpers.Render(2);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"endpoint-{last}.png"), PngBitmapEncoderOptions.Default);
        }
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
    public async Task Points_can_still_be_selected_after_the_playback_state_changes()
    {
        using var editor = await Fixture.Create();
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        var geometryView = editor.View.FindControl<PathGeometryControl>("view")!;
        geometryView.SetCurrentValue(PathGeometryControl.IsPlayingProperty, true);
        geometryView.SetCurrentValue(PathGeometryControl.IsPlayingProperty, false);
        HeadlessTestHelpers.Render(3);
        editor.Click(80, 100);
        Assert.That(editor.Model.SelectedOperation.Value, Is.SameAs(editor.Figure.Segments[0]));
        var curve = (CubicBezierSegment)editor.Figure.Segments[1];
        Assert.That(editor.View.FindThumb(curve, curve.ControlPoint1)!.IsVisible, Is.True);
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
        editor.Figure.IsClosed.CurrentValue = true;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(3));
        Assert.That(((BaseEditorViewModel)editor.Model.PointProperties.Value[2]).PropertyAdapter.GetEngineProperty(),
            Is.SameAs(first.ControlPoint1));
        Assert.That(editor.View.FindThumb(first, first.ControlPoint1)!.IsVisible, Is.True);
        editor.Figure.IsClosed.CurrentValue = false;
        HeadlessTestHelpers.Render(3);
        Assert.That(editor.Model.PointProperties.Value, Has.Count.EqualTo(2));
        Assert.That(editor.View.FindThumb(first, first.ControlPoint1)!.IsVisible, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false, 640)]
    [TestCase(true, 640)]
    [TestCase(false, 320)]
    [TestCase(true, 320)]
    public async Task Render_single_curve_inspector_with_control_points_and_menus(bool light, int width)
    {
        using var editor = await Fixture.Create(light: light);
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
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = editor.Window.CaptureRenderedFrame();
            frame!.Save(System.IO.Path.Combine(directory, $"path-properties-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTest]
    [TestCase(false, 640, 420)]
    [TestCase(true, 640, 420)]
    [TestCase(false, 320, 240)]
    [TestCase(true, 320, 240)]
    public async Task Render_selected_curve_and_marquee(bool light, int width, int height)
    {
        using var editor = await Fixture.Create(light: light);
        editor.Window.Width = width;
        editor.Window.Height = height;
        HeadlessTestHelpers.Render(3);
        editor.Key(Key.B);
        editor.Click(180, 100);
        editor.Key(Key.V);
        editor.Click(280, 180, RawInputModifiers.Shift);
        editor.MouseDown(new(55, 60), MouseButton.Left, RawInputModifiers.Shift);
        editor.MouseMove(new(320, 220), RawInputModifiers.Shift | RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        editor.MouseUp(new(320, 220), MouseButton.Left, RawInputModifiers.Shift);
        editor.MouseMove(new(10, 10));
        HeadlessTestHelpers.Render(3);
        if (Environment.GetEnvironmentVariable("BEUTL_PATH_EDITOR_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = editor.Window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            frame!.Save(System.IO.Path.Combine(directory, $"path-editor-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
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
        var figure = new PathFigure { StartPoint = { CurrentValue = new(80, 100) } };
        figure.Segments.Add(new ArcSegment
        {
            Radius = { CurrentValue = new(rx, ry) },
            RotationAngle = { CurrentValue = rotation },
            IsLargeArc = { CurrentValue = large },
            SweepClockwise = { CurrentValue = clockwise },
            Point = { CurrentValue = new(280, 180) }
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
            context.MoveTo(figure.StartPoint.CurrentValue);
            foreach (var segment in figure.Segments)
            {
                if (segment is ArcSegment arc)
                    context.ArcTo(arc.Radius.CurrentValue, arc.RotationAngle.CurrentValue,
                        arc.IsLargeArc.CurrentValue, arc.SweepClockwise.CurrentValue, arc.Point.CurrentValue);
                else context.LineTo(segment.GetEndPoint().CurrentValue);
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
        var context = new CompositionContext(TimeSpan.Zero);
        BtlPoint[] expected = Enumerable.Range(0, 101).Select(i => PathEditingOperations.Evaluate(default, cubic, i / 100f, context)).ToArray();
        var inserted = PathEditingOperations.Split(figure, 1, .4f, context)!;
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            var actual = t <= .4f ? PathEditingOperations.Evaluate(default, inserted, t / .4f, context)
                : PathEditingOperations.Evaluate(inserted.GetEndPoint().CurrentValue, cubic, (t - .4f) / .6f, context);
            Assert.That(((Beutl.Graphics.Vector)(actual - expected[i])).Length, Is.LessThan(.001));
        }
        cubic.EndPoint.Animation = new KeyFrameAnimation<BtlPoint>();
        Assert.That(PathEditingOperations.Split(figure, 2, .5f, context), Is.Null);
        Assert.That(figure.Segments, Has.Count.EqualTo(3));
    }

    private sealed class Fixture : IDisposable
    {
        public required EditViewModel Editor { get; init; }
        public required PathEditorTabViewModel Model { get; init; }
        public required PathFigure Figure { get; init; }
        public required PathEditorTabView View { get; init; }
        public required Window Window { get; init; }
        private readonly List<IDisposable> _properties = [];
        public PathSegment[] Selected => View.GetSelectedAnchors().Select(t => (PathSegment)t.DataContext!).ToArray();

        public static async Task<Fixture> Create(bool empty = false, bool light = false, PathFigure? pathFigure = null)
        {
            await TestReset.ResetShellAsync();
            string root = System.IO.Path.Combine(BeutlHomeIsolation.CurrentHome!, $"path-edit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "path-edit", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var figure = pathFigure ?? new PathFigure();
            if (!empty && pathFigure == null)
            {
                figure.Segments.Add(new LineSegment(80, 100));
                figure.Segments.Add(new LineSegment(180, 100));
                figure.Segments.Add(new LineSegment(280, 180));
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            var shape = new GeometryShape { Data = { CurrentValue = geometry } };
            var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(10) };
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
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
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

        public static async Task<Fixture> CreateCollapsed(bool last, bool light = false)
        {
            var fixture = await Create(empty: true, light: light);
            var first = PathEditingOperations.Cubic(new(170, 90), new(203.16f, -83.08f), new(203.16f, -83.08f));
            var second = PathEditingOperations.Cubic(first.EndPoint.CurrentValue, new(440, -90), new(430, -20));
            var third = PathEditingOperations.Cubic(new(430, 85), new(310, 155), new(250, 140));
            using (fixture.Editor.HistoryManager.SuppressRecording())
            {
                fixture.Figure.IsClosed.CurrentValue = true;
                fixture.Figure.Segments.Add(first);
                fixture.Figure.Segments.Add(second);
                fixture.Figure.Segments.Add(third);
                if (last)
                {
                    third.ControlPoint2.CurrentValue = third.EndPoint.CurrentValue;
                    first.ControlPoint1.CurrentValue = third.EndPoint.CurrentValue;
                }
            }
            fixture.View.Matrix = Matrix.CreateTranslation(-120, 140);
            HeadlessTestHelpers.Render(3);
            BtlPoint selected = last ? third.EndPoint.CurrentValue : first.EndPoint.CurrentValue;
            fixture.Click(selected.X - 120, selected.Y + 140);
            HeadlessTestHelpers.Render(3);
            return fixture;
        }

        private Point WindowPoint(Point point) => ((Control)Window.Content!).FindControl<Canvas>("canvas")!.TranslatePoint(point, Window)!.Value;

        public void MouseDown(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseDown(WindowPoint(point), button, modifiers);

        public void MouseUp(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseUp(WindowPoint(point), button, modifiers);

        public void MouseMove(Point point, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseMove(WindowPoint(point), modifiers);

        public void MouseWheel(Point point, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseWheel(WindowPoint(point), delta, modifiers);

        public void ClickControl(Control control)
        {
            control.BringIntoView();
            HeadlessTestHelpers.Render(2);
            var top = TopLevel.GetTopLevel(control)!;
            Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
            top.MouseMove(point);
            top.MouseDown(point, MouseButton.Left);
            top.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
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
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
            foreach (var property in _properties) property.Dispose();
        }
    }
}

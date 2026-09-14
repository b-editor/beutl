using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorRulerBarTests
{
    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(960, false)]
    [TestCase(640, true)]
    [TestCase(960, true)]
    public async Task Both_rulers_stay_aligned_during_seek_pan_zoom_and_keyframe_drag(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"graph-rulers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "graph-rulers", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var animation = new KeyFrameAnimation<float>();
        var first = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.5), Value = 100 };
        animation.KeyFrames.Add(first);
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.5), Value = 500 });
        var shape = new RectShape();
        shape.Width.Animation = animation;
        await ((IElementAdder)editor.GetService(typeof(IElementAdder))!).AddAsync(
            [new ElementDescription(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => shape))], CancellationToken.None);
        Element element = scene.Children.Single();
        using var model = new GraphEditorTabViewModel(editor);
        var view = new GraphEditorTabView { DataContext = model };
        var window = new Window
        {
            Content = view, Width = width, Height = 420,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        GraphEditorViewModel? graph = null;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            GraphEditorView graphView = view.GetVisualDescendants().OfType<GraphEditorView>().Single();
            Assert.That(graphView.IsVisible, Is.False);
            model.Element.Value = element;
            model.Select(animation);
            HeadlessTestHelpers.Render();
            graph = model.SelectedAnimation.Value!;
            graph.Options.Value = graph.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
            graph.ScrollOffset.Value = new Vector(0, 60);
            scene.Duration = TimeSpan.FromSeconds(2);
            HeadlessTestHelpers.Render();
            Assert.That(graphView.IsEffectivelyVisible, Is.True);

            Border bar = graphView.FindControl<Border>("RulerBar")!;
            TimelineScale horizontal = graphView.FindControl<TimelineScale>("scale")!;
            GraphEditorScale vertical = graphView.FindControl<GraphEditorScale>("verticalScale")!;
            ScrollViewer scroll = graphView.FindControl<ScrollViewer>("scroll")!;
            Panel canvas = graphView.FindControl<Panel>("graphPanel")!;
            GraphEditorBackground background = graphView.FindControl<GraphEditorBackground>("background")!;
            CheckAlignment();
            Capture("selected");

            Click(horizontal, new Point(75, 24));
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            Click(bar, new Point(90, 4));
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.6)));

            Drag(horizontal, new Point(298, 8), new Point(270, 8));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.8)));
            Drag(horizontal, new Point(2, 8), new Point(30, 8));
            Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(0.2)));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.6)));

            // Grab the actual keyframe at the intersection of its time and value coordinates.
            Point keyframe = canvas.TranslatePoint(new Point(
                (first.KeyTime + element.Start).TimeToPixel(graph.Options.Value.Scale),
                graph.Baseline.Value - first.Value * graph.ScaleY.Value), window)!.Value;
            window.MouseMove(keyframe);
            window.MouseDown(keyframe, MouseButton.Left);
            Assert.That(graphView.KeyTimeMoveState, Is.Not.Null);
            window.MouseMove(keyframe + new Vector(30, -20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(keyframe + new Vector(30, -20), MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(first.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.7)));
            Assert.That(first.Value, Is.EqualTo(140).Within(0.001));
            CheckAlignment();

            graph.ScrollOffset.Value = new Vector(75, 60);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.X, Is.EqualTo(75));
            Assert.That(scroll.Offset.Y, Is.EqualTo(60));
            CheckAlignment();
            Click(bar, new Point(75, 4));
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));

            Point verticalPoint = vertical.TranslatePoint(new Point(10, 80), window)!.Value;
            double previousY = scroll.Offset.Y;
            Vector pan = GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection
                ? new Vector(-1, 0) : new Vector(0, -1);
            window.MouseWheel(verticalPoint, pan);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.Y, Is.GreaterThan(previousY));
            Assert.That(scroll.Offset.X, Is.EqualTo(75));
            CheckAlignment();

            RawInputModifiers modifier = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
                ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.MouseWheel(verticalPoint, new Vector(0, 1), modifier);
            HeadlessTestHelpers.Render();
            Assert.That(graph.ScaleY.Value, Is.EqualTo(0.6).Within(0.001));
            CheckAlignment();
            window.MouseWheel(bar.TranslatePoint(new Point(100, 4), window)!.Value, new Vector(0, 1), modifier);
            HeadlessTestHelpers.Render();
            Assert.That(graph.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.001));
            CheckAlignment();
            Capture("zoomed");

            window.Height = 320;
            HeadlessTestHelpers.Render();
            CheckAlignment();
            model.Select(null);
            HeadlessTestHelpers.Render();
            Assert.That(graphView.IsVisible, Is.False);

            GraphEditorViewModel previousGraph = graph;
            model.Select(animation);
            graph = model.SelectedAnimation.Value!;
            HeadlessTestHelpers.Render();
            graph.ScrollOffset.Value = new Vector(25, 40);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset, Is.EqualTo(new Vector(25, 40)));
            previousGraph.ScrollOffset.Value = default;
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset, Is.EqualTo(new Vector(25, 40)));
            previousGraph.Dispose();
            CheckAlignment();

            void CheckAlignment()
            {
                Point plotOrigin = scroll.TranslatePoint(default, graphView)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(bar.Bounds.Height, Is.EqualTo(38));
                    Assert.That(horizontal.Bounds.Height, Is.EqualTo(32));
                    Assert.That(horizontal.TranslatePoint(default, bar)!.Value.Y, Is.EqualTo(6));
                    Assert.That(horizontal.TranslatePoint(default, graphView)!.Value.X, Is.EqualTo(plotOrigin.X));
                    Assert.That(horizontal.TranslatePoint(new Point(0, horizontal.Bounds.Height), graphView)!.Value.Y, Is.EqualTo(plotOrigin.Y));
                    Assert.That(vertical.TranslatePoint(default, graphView)!.Value.Y, Is.EqualTo(plotOrigin.Y));
                    Assert.That(vertical.Bounds.Width, Is.EqualTo(20));
                    Assert.That(vertical.Bounds.Height, Is.EqualTo(scroll.Viewport.Height));
                    Assert.That(vertical.Bounds.Height, Is.LessThanOrEqualTo(scroll.Bounds.Height));
                    Assert.That(vertical.TranslatePoint(new Point(vertical.Bounds.Width, 0), graphView)!.Value.X, Is.EqualTo(plotOrigin.X));
                    Assert.That(vertical.Baseline, Is.EqualTo(background.Baseline));
                    Assert.That(vertical.Scale, Is.EqualTo(background.Scale));
                    Assert.That(vertical.Offset.Y, Is.EqualTo(scroll.Offset.Y));
                    Assert.That(horizontal.Offset.X, Is.EqualTo(scroll.Offset.X));
                    Assert.That(horizontal.TranslatePoint(new Point(100, 0), graphView)!.Value.X,
                        Is.EqualTo(canvas.TranslatePoint(new Point(100 + scroll.Offset.X, 0), graphView)!.Value.X).Within(0.001));
                    Assert.That(vertical.TranslatePoint(new Point(0, graph.Baseline.Value - scroll.Offset.Y), graphView)!.Value.Y,
                        Is.EqualTo(background.TranslatePoint(new Point(0, graph.Baseline.Value), graphView)!.Value.Y).Within(0.001));
                });
            }

            void Click(Control control, Point point)
            {
                Point position = control.TranslatePoint(point, window)!.Value;
                window.MouseMove(position);
                window.MouseDown(position, MouseButton.Left);
                window.MouseUp(position, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }

            void Drag(Control control, Point from, Point to)
            {
                Point start = control.TranslatePoint(from, window)!.Value;
                Point end = control.TranslatePoint(to, window)!.Value;
                window.MouseMove(start);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }
        }
        finally
        {
            view.DataContext = null;
            graph?.Dispose();
            window.Close();
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_RULER_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}

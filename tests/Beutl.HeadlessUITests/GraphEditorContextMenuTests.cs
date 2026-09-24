using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorContextMenuTests
{
    [AvaloniaTest]
    [TestCase("ControlPoint1", false)]
    [TestCase("ControlPoint2", false)]
    [TestCase("ControlPoint1", true)]
    [TestCase("ControlPoint2", true)]
    public async Task OverlappingControlPoint_OpensTheCoveredKeyFramesMenu(string tag, bool light)
    {
        using var graph = await GraphScope.CreateAsync(light);
        AvaloniaPath handle = graph.Handle(tag);
        IKeyFrame expected = tag == "ControlPoint1" ? graph.First : graph.Second;
        AvaloniaPath keyFrame = graph.KeyFrame(expected);
        Point position = handle.TranslatePoint(default, graph.Window)!.Value;
        Assert.That(graph.HitTest(position), Is.SameAs(handle),
            "The control point must cover the keyframe to reproduce the regression.");

        graph.RightClick(position);
        graph.Capture($"overlap-{tag}-{light}");

        Assert.Multiple(() =>
        {
            Assert.That(keyFrame.ContextMenu!.IsOpen, Is.True);
            Assert.That(graph.BackgroundMenu.IsOpen, Is.False);
            Assert.That(graph.View.ControlPointMoveState, Is.Null);
            Assert.That(graph.View.KeyTimeMoveState, Is.Null);
        });

        ContextMenu menu = keyFrame.ContextMenu!;
        var model = (GraphEditorKeyFrameViewModel)keyFrame.DataContext!;
        Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Header),
            Is.EqualTo(new[] { Strings.Copy, Strings.Paste, Strings.Delete }));
        MenuItem delete = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Delete));
        Assert.That(delete.Command, Is.SameAs(model.RemoveCommand));
        delete.Command!.Execute(delete.CommandParameter);
        HeadlessTestHelpers.Render();
        Assert.That(graph.Animation.KeyFrames, Does.Not.Contain(expected));
        Assert.That(graph.Animation.KeyFrames, Does.Contain(expected == graph.First ? graph.Second : graph.First));
    }

    [AvaloniaTest]
    [TestCase("ControlPoint1")]
    [TestCase("ControlPoint2")]
    public async Task SeparateControlPointAndEmptySpace_KeepTheBackgroundMenu(string tag)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        AvaloniaPath handle = graph.Handle(tag);
        Point position = handle.TranslatePoint(default, graph.Window)!.Value;
        Assert.That(graph.HitTest(position), Is.SameAs(handle));
        graph.RightClick(position);
        Assert.That(graph.BackgroundMenu.IsOpen, Is.True);
        Assert.That(graph.KeyFrame(graph.Second).ContextMenu!.IsOpen, Is.False);
        graph.BackgroundMenu.Close();

        graph.RightClick(new Point(500, 200));
        Assert.That(graph.BackgroundMenu.IsOpen, Is.True);
        graph.BackgroundMenu.Close();

        AvaloniaPath keyFrame = graph.KeyFrame(graph.Second);
        position = keyFrame.TranslatePoint(default, graph.Window)!.Value;
        Assert.That(graph.HitTest(position), Is.SameAs(keyFrame));
        graph.RightClick(position);
        Assert.That(keyFrame.ContextMenu!.IsOpen, Is.True);
        Assert.That(graph.BackgroundMenu.IsOpen, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task OverlappingControlPoint_PreservesLeftDragAndAltDrag(bool alt)
    {
        using var graph = await GraphScope.CreateAsync();
        AvaloniaPath handle = graph.Handle("ControlPoint2");
        Point position = handle.TranslatePoint(default, graph.Window)!.Value;
        RawInputModifiers modifiers = alt ? RawInputModifiers.Alt : RawInputModifiers.None;
        graph.Window.MouseMove(position, modifiers);
        graph.Window.MouseDown(position, MouseButton.Left, modifiers);
        Assert.Multiple(() =>
        {
            Assert.That(graph.View.ControlPointMoveState is not null, Is.EqualTo(alt));
            Assert.That(graph.View.KeyTimeMoveState is not null, Is.EqualTo(!alt));
        });
        Point end = position + new Vector(-15, 15);
        graph.Window.MouseMove(end, modifiers | RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(end, MouseButton.Left, modifiers);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(alt ? 1.5 : 1.4)));
            Assert.That(graph.Second.Value, Is.EqualTo(alt ? 500 : 470).Within(0.001));
            Assert.That(graph.View.ControlPointMoveState, Is.Null);
            Assert.That(graph.View.KeyTimeMoveState, Is.Null);
            Assert.That(graph.BackgroundMenu.IsOpen, Is.False);
        });
        if (alt)
        {
            var easing = (SplineEasing)graph.Second.Easing;
            Assert.That(easing.X2, Is.LessThan(1));
            Assert.That(easing.Y2, Is.LessThan(1));
        }
    }

    private sealed class GraphScope : IDisposable
    {
        public required KeyFrameAnimation<float> Animation { get; init; }
        public required KeyFrame<float> First { get; init; }
        public required KeyFrame<float> Second { get; init; }
        public required GraphEditorViewModel Model { get; init; }
        public required GraphEditorView View { get; init; }
        public required Window Window { get; init; }
        public ContextMenu BackgroundMenu => View.FindControl<Panel>("graphPanel")!.ContextMenu!;

        public static async Task<GraphScope> CreateAsync(bool light = false, bool separateHandles = false)
        {
            await TestReset.ResetShellAsync();
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"graph-context-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "graph-context", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var animation = new KeyFrameAnimation<float>();
            var first = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.5), Value = 100 };
            var second = new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(1.5),
                Value = 500,
                Easing = separateHandles ? new SplineEasing(0.25f, 0.25f, 0.75f, 0.75f) : new SplineEasing(0, 0, 1, 1)
            };
            animation.KeyFrames.Add(first);
            animation.KeyFrames.Add(second);
            var shape = new RectShape();
            shape.Width.Animation = animation;
            await ((IElementAdder)editor.GetService(typeof(IElementAdder))!).AddAsync(
                [new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
                    new ElementSource.EngineObject(() => shape))], CancellationToken.None);
            var model = new GraphEditorViewModel<float>(editor, animation, scene.Children.Single());
            model.Options.Value = model.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
            var view = new GraphEditorView { DataContext = model };
            var window = new Window
            {
                Content = view,
                Width = 640,
                Height = 420,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
            };
            window.Show();
            HeadlessTestHelpers.Render();
            return new GraphScope
            {
                Animation = animation,
                First = first,
                Second = second,
                Model = model,
                View = view,
                Window = window
            };
        }

        public AvaloniaPath Handle(string tag) => View.GetVisualDescendants().OfType<AvaloniaPath>()
            .Single(path => Equals(path.Tag, tag)
                && path.DataContext is GraphEditorKeyFrameViewModel model && model.Model == Second);

        public AvaloniaPath KeyFrame(IKeyFrame keyFrame) => View.GetVisualDescendants().OfType<AvaloniaPath>()
            .Single(path => path.Name == "KeyTimeIcon"
                && path.DataContext is GraphEditorKeyFrameViewModel model && model.Model == keyFrame);

        // Avalonia answers a hit test from the compositor's readback of a rendered frame, and answers
        // null for the whole window until one exists. The tick forced right after Show often precedes
        // the window's first frame, so wait for the window to answer before asserting what it hits.
        public IInputElement? HitTest(Point position)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (Window.InputHitTest(position) is { } hit)
                    return hit;

                HeadlessTestHelpers.Render();
            }

            return Window.InputHitTest(position);
        }

        public void RightClick(Point position)
        {
            HitTest(position);
            Window.MouseMove(position);
            Window.MouseDown(position, MouseButton.Right);
            Window.MouseUp(position, MouseButton.Right);
            HeadlessTestHelpers.Render();
        }

        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_CONTEXT_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = Window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
            ContextMenu? menu = View.GetVisualDescendants().OfType<Control>()
                .Select(control => control.ContextMenu).FirstOrDefault(menu => menu?.IsOpen == true);
            if (menu is null) return;
            using var popup = TopLevel.GetTopLevel(menu)?.CaptureRenderedFrame();
            popup?.Save(Path.Combine(directory, name + "-menu.png"), PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            BackgroundMenu.Close();
            foreach (ContextMenu menu in View.GetVisualDescendants().OfType<Control>()
                         .Select(control => control.ContextMenu).OfType<ContextMenu>())
                menu.Close();
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
        }
    }
}

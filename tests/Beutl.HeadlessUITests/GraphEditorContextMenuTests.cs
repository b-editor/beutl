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
using FluentAvalonia.UI.Controls;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorContextMenuTests
{
    [AvaloniaTest]
    [TestCase(KeyModifiers.Control)]
    [TestCase(KeyModifiers.Meta)]
    public async Task ContextMenu_ShowsEveryShortcutWithThePlatformCommandModifier(KeyModifiers command)
    {
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        KeyModifiers previous = hotkeys.CommandModifiers;
        hotkeys.CommandModifiers = command;
        try
        {
            using var graph = await GraphScope.CreateAsync();
            graph.RightClick(new Point(500, 200));
            Assert.That(graph.BackgroundMenu.IsOpen, Is.True);
            var expected = new Dictionary<string, (Key Key, KeyModifiers Modifiers)>
            {
                ["Ease"] = (Key.F9, KeyModifiers.None),
                ["EaseIn"] = (Key.F9, KeyModifiers.Shift),
                ["EaseOut"] = (Key.F9, command),
                ["Velocity"] = (Key.K, command | KeyModifiers.Shift),
                ["DistributeEvenly"] = (Key.D, KeyModifiers.Alt),
                ["Reverse"] = (Key.R, KeyModifiers.Alt)
            };
            Assert.Multiple(() =>
            {
                foreach (var (tag, gesture) in expected)
                {
                    FAMenuFlyoutItem item = graph.BackgroundMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Tag, tag));
                    Assert.That(item.InputGesture, Is.Not.Null, tag);
                    Assert.That(item.InputGesture?.Key, Is.EqualTo(gesture.Key), tag);
                    Assert.That(item.InputGesture?.KeyModifiers, Is.EqualTo(gesture.Modifiers), tag);
                    Assert.That(item.Text?.ToString(), Does.Not.Contain("(").And.Not.Contain(")"), tag);
                }
            });
            graph.Capture($"shortcuts-{command}");
        }
        finally
        {
            hotkeys.CommandModifiers = previous;
        }
    }

    [AvaloniaTest]
    [TestCase("FitAll", "F", false)]
    [TestCase("FitSelection", "Shift+F", false)]
    [TestCase("FitAll", "F", true)]
    [TestCase("FitSelection", "Shift+F", true)]
    public async Task FitButtons_KeepShortcutHintsSeparateFromLabels(string tag, string shortcut, bool light)
    {
        using var graph = await GraphScope.CreateAsync(light);
        Button button = graph.View.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, tag));
        string label = tag == "FitAll" ? Strings.GraphFitAll : Strings.GraphFitSelection;
        Assert.That(label, Does.Not.Contain("(").And.Not.Contain(")"));
        ToolTip.SetIsOpen(button, true);
        try
        {
            HeadlessTestHelpers.Render(3);
            Assert.That(ToolTip.GetTip(button), Is.InstanceOf<Control>(),
                "The localized label and shortcut must be displayed separately in the tooltip.");
            var content = (Control)ToolTip.GetTip(button)!;
            TextBlock[] text = content.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.That(text.Select(block => block.Text), Is.EqualTo(new[] { label, shortcut }));
            foreach (TextBlock block in text)
            {
                Assert.That(block.IsEffectivelyVisible, Is.True);
                Assert.That(block.Bounds.Width, Is.GreaterThan(0));
            }
            if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_CONTEXT_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = TopLevel.GetTopLevel(content)?.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"fit-tooltip-{tag}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
        }
    }

    [AvaloniaTest]
    [TestCase("ControlPoint1", false)]
    [TestCase("ControlPoint2", false)]
    [TestCase("ControlPoint1", true)]
    [TestCase("ControlPoint2", true)]
    public async Task OverlappingControlPoint_OpensTheCoveredKeyFramesMenu(string tag, bool light)
    {
        using var graph = await GraphScope.CreateAsync(light);
        IKeyFrame expected = tag == "ControlPoint1" ? graph.First : graph.Second;
        graph.Model.SelectedView.Value!.SetSelection([expected]);
        HeadlessTestHelpers.Render();
        AvaloniaPath handle = graph.Handle(tag);
        AvaloniaPath keyFrame = graph.KeyFrame(expected);
        Point position = handle.TranslatePoint(default, graph.Window)!.Value;
        Assert.That(graph.HitTest(position), Is.SameAs(handle),
            "The control point must cover the keyframe to reproduce the regression.");

        graph.RightClick(position);
        graph.Capture($"overlap-{tag}-{light}");

        Assert.Multiple(() =>
        {
            Assert.That(keyFrame.ContextFlyout!.IsOpen, Is.True);
            Assert.That(graph.BackgroundMenu.IsOpen, Is.False);
            Assert.That(graph.View.ControlPointMoveState, Is.Null);
            Assert.That(graph.View.KeyTimeMoveState, Is.Null);
        });

        FAMenuFlyout menu = (FAMenuFlyout)keyFrame.ContextFlyout!;
        var model = (GraphEditorKeyFrameViewModel)keyFrame.DataContext!;
        Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text),
            Is.EqualTo(new[] { Strings.Copy, Strings.Paste, Strings.Delete }));
        FAMenuFlyoutItem delete = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Text, Strings.Delete));
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
        Assert.That(graph.KeyFrame(graph.Second).ContextFlyout!.IsOpen, Is.False);
        graph.BackgroundMenu.Hide();

        graph.RightClick(new Point(500, 200));
        Assert.That(graph.BackgroundMenu.IsOpen, Is.True);
        graph.BackgroundMenu.Hide();

        AvaloniaPath keyFrame = graph.KeyFrame(graph.Second);
        position = keyFrame.TranslatePoint(default, graph.Window)!.Value;
        Assert.That(graph.HitTest(position), Is.SameAs(keyFrame));
        graph.RightClick(position);
        Assert.That(keyFrame.ContextFlyout!.IsOpen, Is.True);
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

    internal sealed class GraphScope : IDisposable
    {
        public required KeyFrameAnimation<float> Animation { get; init; }
        public required KeyFrame<float> First { get; init; }
        public required KeyFrame<float> Second { get; init; }
        public required GraphEditorViewModel Model { get; init; }
        public required GraphEditorView View { get; init; }
        public required Window Window { get; init; }
        public FAMenuFlyout BackgroundMenu => (FAMenuFlyout)View.FindControl<Panel>("graphPanel")!.ContextFlyout!;

        public static async Task<GraphScope> CreateAsync(bool light = false, bool separateHandles = false, bool selectAll = true)
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
            if (selectAll) model.SelectedView.Value!.SetSelection(animation.KeyFrames);
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
            FAMenuFlyout? menu = View.GetVisualDescendants().OfType<Control>()
                .Select(control => control.ContextFlyout).OfType<FAMenuFlyout>().FirstOrDefault(menu => menu.IsOpen);
            if (menu is null) return;
            using var popup = TopLevel.GetTopLevel(menu.Popup.Child!)?.CaptureRenderedFrame();
            popup?.Save(Path.Combine(directory, name + "-menu.png"), PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            BackgroundMenu.Hide();
            foreach (FAMenuFlyout menu in View.GetVisualDescendants().OfType<Control>()
                         .Select(control => control.ContextFlyout).OfType<FAMenuFlyout>())
                menu.Hide();
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
        }
    }
}

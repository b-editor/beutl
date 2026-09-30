using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NodeGraphInputTests
{
    [AvaloniaTest]
    [TestCase(Key.OemPlus, RawInputModifiers.Shift, "+")]
    [TestCase(Key.OemPlus, RawInputModifiers.None, "=")]
    [TestCase(Key.Add, RawInputModifiers.None, "+")]
    [TestCase(Key.OemMinus, RawInputModifiers.None, "-")]
    [TestCase(Key.Subtract, RawInputModifiers.None, "-")]
    public async Task Typing_in_a_node_does_not_invoke_graph_shortcuts(Key key, RawInputModifiers modifiers, string text)
    {
        using GraphSession session = await GraphSession.CreateAsync();
        TextBox input = session.View.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.That(input.Focus(), Is.True);
        input.CaretIndex = input.Text!.Length;
        Matrix before = session.Zoom.Matrix;

        KeyEventArgs args = session.PressKey(key, modifiers, text);
        Assert.Multiple(() =>
        {
            Assert.That(args.Handled, Is.False, "The native input method must still be allowed to deliver the character.");
            Assert.That(session.Zoom.Matrix, Is.EqualTo(before));
        });
        session.Window.KeyTextInput(text);
        Assert.That(input.Text, Is.EqualTo("1" + text));

        // Caret navigation belongs to the editor, too.
        session.PressKey(Key.Home);
        Assert.That(input.CaretIndex, Is.Zero);
        Assert.That(session.Zoom.Matrix, Is.EqualTo(before));
        await session.Capture($"typing-{key}-{modifiers}");

        // The same shortcut is still available after focus returns to the graph.
        Assert.That(session.Zoom.Focus(), Is.True);
        Assert.That(session.PressKey(Key.OemPlus).Handled, Is.True);
        Assert.That(session.Zoom.ZoomX, Is.EqualTo(before.M11 * session.Zoom.KeyboardZoomStep).Within(0.0001));
    }

    [AvaloniaTest]
    public async Task Custom_text_input_descendants_do_not_invoke_graph_shortcuts()
    {
        using GraphSession session = await GraphSession.CreateAsync();
        var input = new Border { Focusable = true, Width = 100, Height = 30 };
        var editor = new Border { Child = input };
        ContextCommandInput.SetIsTextInput(editor, true);
        session.Canvas.Children.Add(editor);
        HeadlessTestHelpers.Render();
        Assert.That(input.Focus(), Is.True);
        Matrix before = session.Zoom.Matrix;
        Assert.That(session.PressKey(Key.OemPlus).Handled, Is.False);
        Assert.That(session.Zoom.Matrix, Is.EqualTo(before));
    }

    [AvaloniaTest]
    [TestCase(0.5, 320, false)]
    [TestCase(1, 640, false)]
    [TestCase(2, 640, true)]
    public async Task Touchpad_scroll_follows_both_axes_without_zoom_or_animation(double scale, int width, bool light)
    {
        using GraphSession session = await GraphSession.CreateAsync(_ => true);
        session.Window.Width = width;
        session.Window.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        session.Zoom.EnableAnimations = true;
        HeadlessTestHelpers.Render(3);
        session.Zoom.SetMatrix(new Matrix(scale, 0, 0, scale, 45, 30), true);
        await session.Capture($"scroll-before-{width}-{light}");
        foreach (RawInputModifiers modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
        {
            foreach (Vector delta in new[] { new Vector(0, -1), new Vector(-1, 0), new Vector(-0.4, -0.7), new Vector(0.25, 0.5) })
            {
                session.Zoom.SetMatrix(new Matrix(scale, 0, 0, scale, 45, 30), true);
                Point point = new(100, 80);
                Point before = session.Canvas.TranslatePoint(point, session.Window)!.Value;
                session.Scroll(delta, modifiers);
                Assert.Multiple(() =>
                {
                    Assert.That(session.Zoom.ZoomX, Is.EqualTo(scale));
                    Assert.That(session.Zoom.ZoomY, Is.EqualTo(scale));
                    Assert.That(session.Zoom.OffsetX, Is.EqualTo(45 + delta.X * 50).Within(0.001));
                    Assert.That(session.Zoom.OffsetY, Is.EqualTo(30 + delta.Y * 50).Within(0.001));
                    Assert.That(session.Model.Matrix.Value, Is.EqualTo(session.Zoom.Matrix));
                });
                session.AssertRenderedPoint(point, before + delta * 50);
            }
        }
        await session.Capture($"scroll-after-{width}-{light}");
    }

    [AvaloniaTest]
    public async Task Switching_input_devices_keeps_mouse_wheel_zoom_and_touchpad_pan()
    {
        bool touchpad = true;
        using GraphSession session = await GraphSession.CreateAsync(_ => touchpad);
        session.Scroll(new Vector(0, -0.25));
        Assert.That(session.Zoom.ZoomX, Is.EqualTo(1));
        Assert.That(session.Zoom.OffsetY, Is.EqualTo(-12.5));

        touchpad = false;
        session.Scroll(new Vector(0, 1));
        Assert.That(session.Zoom.ZoomX, Is.EqualTo(1.2).Within(0.0001));
        Matrix before = session.Zoom.Matrix;
        touchpad = true;
        session.Scroll(new Vector(-0.25, 0));
        Assert.That(session.Zoom.ZoomX, Is.EqualTo(before.M11));
        Assert.That(session.Zoom.OffsetX, Is.EqualTo(before.M31 - 12.5).Within(0.001));
        Assert.That(session.Zoom.OffsetY, Is.EqualTo(before.M32));
    }

    [AvaloniaTest]
    public async Task Command_scroll_keeps_zoom_anchored_to_pointer()
    {
        using GraphSession session = await GraphSession.CreateAsync(
            _ => throw new AssertionException("Command zoom must not depend on the input device."));
        session.Zoom.SetMatrix(new Matrix(1.5, 0, 0, 1.5, 45, 30), true);
        session.Zoom.EnableAnimations = true;
        Point anchor = session.Window.TranslatePoint(session.ScrollPosition, session.Canvas)!.Value;
        RawInputModifiers modifier = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;
        session.Scroll(new Vector(0, 1), modifier);
        Assert.That(session.Zoom.ZoomX, Is.EqualTo(1.8).Within(0.0001));
        session.AssertRenderedPoint(anchor, session.ScrollPosition);
    }

    [AvaloniaTest]
    [TestCase(0.5)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Touchpad_pinch_uses_native_magnification_and_keeps_the_pointer_anchored(double scale)
    {
        using GraphSession session = await GraphSession.CreateAsync();
        session.Zoom.SetMatrix(new Matrix(scale, 0, 0, scale, 45, 30), true);
        session.Zoom.EnableAnimations = true;
        Point position = new(180, 120);
        Point anchor = session.Window.TranslatePoint(position, session.Canvas)!.Value;
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        foreach (double delta in new[] { 0.2, -0.1, 0.001 })
        {
            var args = new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent,
                session.Canvas, pointer, session.Window, position, 1, default, KeyModifiers.None, new Vector(delta, delta));
            session.Canvas.RaiseEvent(args);
            scale *= 1 + delta;
            Assert.Multiple(() =>
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(session.Zoom.ZoomX, Is.EqualTo(scale).Within(0.0001));
                Assert.That(session.Zoom.ZoomY, Is.EqualTo(scale).Within(0.0001));
                Assert.That(session.Model.Matrix.Value, Is.EqualTo(session.Zoom.Matrix));
            });
            session.AssertRenderedPoint(anchor, position);
        }
        await session.Capture($"pinch-{scale}");
    }

    [AvaloniaTest]
    public void Native_input_registration_is_released_on_detach_and_window_close()
    {
        int registrations = 0;
        int removals = 0;
        var view = new NodeGraphView(_ => false, _ =>
        {
            registrations++;
            return System.Reactive.Disposables.Disposable.Create(() => removals++);
        });
        var first = new Window { Content = view };
        var second = new Window();
        try
        {
            first.Show();
            Assert.That(registrations, Is.EqualTo(1));
            first.Content = null;
            Assert.That(removals, Is.EqualTo(1));
            second.Content = view;
            second.Show();
            Assert.That(registrations, Is.EqualTo(2));
            second.Close();
            Assert.That(removals, Is.EqualTo(2));
        }
        finally
        {
            first.Close();
            second.Close();
        }
        Assert.That(removals, Is.EqualTo(2));
    }

    private sealed class GraphSession : IDisposable
    {
        private GraphSession(NodeGraphViewModel model, Func<PointerWheelEventArgs, bool>? detector)
        {
            Model = model;
            View = detector is null ? new NodeGraphView() : new NodeGraphView(detector);
            View.DataContext = model;
            Window = new Window { Content = View, Width = 640, Height = 360, RequestedThemeVariant = ThemeVariant.Dark };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Zoom = View.FindControl<ZoomBorder>("zoomBorder")!;
            Canvas = View.FindControl<Canvas>("canvas")!;
        }

        public NodeGraphViewModel Model { get; }
        public NodeGraphView View { get; }
        public Window Window { get; }
        public ZoomBorder Zoom { get; }
        public Canvas Canvas { get; }
        public Point ScrollPosition => new(Window.ClientSize.Width - 30, Window.ClientSize.Height - 30);

        public static async Task<GraphSession> CreateAsync(Func<PointerWheelEventArgs, bool>? detector = null)
        {
            await TestReset.ResetShellAsync();
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"node-input-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "node-input", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var expression = new ExpressionNode { Position = (24, 24) };
            expression.Expression.Property!.SetValue("1");
            var output = new OutputNode { Position = (320, 96) };
            var graph = new GraphModel();
            graph.Nodes.AddRange([expression, output]);
            graph.Connect(output.InputPort, expression.Output);
            return new GraphSession(new NodeGraphViewModel(graph, editor), detector);
        }

        public void Scroll(Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseWheel(ScrollPosition, delta, modifiers);

        public KeyEventArgs PressKey(Key key, RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
        {
            KeyEventArgs? received = null;
            void Observe(object? sender, KeyEventArgs args) => received = args;
            Window.AddHandler(InputElement.KeyDownEvent, Observe, RoutingStrategies.Bubble, handledEventsToo: true);
            try
            {
                Window.KeyPress(key, modifiers, PhysicalKey.None, symbol);
                Window.KeyRelease(key, modifiers, PhysicalKey.None, symbol);
            }
            finally
            {
                Window.RemoveHandler(InputElement.KeyDownEvent, Observe);
            }
            return received ?? throw new AssertionException("No key event reached the focused control.");
        }

        public void AssertRenderedPoint(Point canvasPoint, Point expected)
        {
            Point actual = Canvas.TranslatePoint(canvasPoint, Window)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.001));
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.001));
            });
        }

        public async Task Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_NODE_INPUT_CAPTURE") is not { Length: > 0 } directory) return;
            // Let the existing node/editor entrance transitions finish before capturing.
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);
            Directory.CreateDirectory(directory);
            using var image = Window.CaptureRenderedFrame();
            Assert.That(image, Is.Not.Null);
            image!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
        }
    }
}

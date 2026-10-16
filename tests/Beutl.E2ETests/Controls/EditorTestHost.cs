using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

/// <summary>
/// Hosts a single <see cref="PropertyEditor"/> in a shown headless window together with a
/// focusable sink, so focus-loss confirmation paths can be exercised by moving focus to the sink.
/// </summary>
internal sealed class EditorTestHost<TEditor> : IDisposable
    where TEditor : PropertyEditor
{
    private readonly ControlHost _host;

    public EditorTestHost(TEditor editor, double width = 480, double height = 200)
    {
        Editor = editor;
        Sink = new Button { Content = "sink" };
        _host = new ControlHost(new StackPanel { Children = { editor, Sink } }, width, height);
    }

    public void Dispose() => _host.Dispose();

    public TEditor Editor { get; }

    public Window Window => _host.Window;

    public Button Sink { get; }

    public T Require<T>(string name)
        where T : Control
    {
        T? found = Editor.FindDescendantOfType<T>(false, control => control.Name == name);
        Assert.That(found, Is.Not.Null, $"Template part '{name}' was not found.");
        return found!;
    }

    public T RequireDescendant<T>()
        where T : Control
    {
        T? found = Editor.FindDescendantOfType<T>();
        Assert.That(found, Is.Not.Null, $"No descendant of type {typeof(T).Name} was found.");
        return found!;
    }

    public void TypeInto(TextBox box, string text)
    {
        box.Focus();
        HeadlessTestHelpers.Settle();
        box.Clear();
        HeadlessTestHelpers.Settle();
        Window.KeyTextInput(text);
        HeadlessTestHelpers.Settle();
    }

    public void WheelOver(Control control, double deltaY)
    {
        control.Focus();
        HeadlessTestHelpers.Settle();
        Point center = Center(control);
        Window.MouseMove(center);
        HeadlessTestHelpers.Settle();
        Window.MouseWheel(center, new Vector(0, deltaY));
        HeadlessTestHelpers.Settle();
    }

    public void Click(Control control)
    {
        Point center = Center(control);
        Window.MouseDown(center, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Window.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }

    // Presses at the control's center, or at a point of the control when one is given.
    public void DragHorizontally(Control control, double deltaX, Point? at = null)
    {
        // Lay out first so the center is real, and let the hover settle before the press.
        HeadlessTestHelpers.Settle();
        Point start = at is { } point ? control.TranslatePoint(point, Window)!.Value : Center(control);
        Point end = start + new Vector(deltaX, 0);
        Window.MouseMove(start);
        HeadlessTestHelpers.Settle();
        Window.MouseDown(start, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Settle();
        Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }

    // Presses on the control and drags without releasing, returning the pointer so a test can take its capture.
    public IPointer PressAndDrag(Control control, double deltaX)
    {
        IPointer? pointer = null;
        void OnPressed(object? sender, PointerPressedEventArgs e) => pointer = e.Pointer;
        Window.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        try
        {
            HeadlessTestHelpers.Settle();
            Point start = Center(control);
            Window.MouseMove(start);
            HeadlessTestHelpers.Settle();
            Window.MouseDown(start, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Window.MouseMove(start + new Vector(deltaX, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle();
        }
        finally
        {
            Window.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        }

        Assert.That(pointer, Is.Not.Null, "The press did not reach the window.");
        return pointer!;
    }

    public void MoveFocusToSink()
    {
        Sink.Focus();
        HeadlessTestHelpers.Settle();
    }

    private Point Center(Control control)
    {
        Point? p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window);
        Assert.That(p, Is.Not.Null, "Control is not connected to the window.");
        return p!.Value;
    }
}

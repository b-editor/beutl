using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    public EditorTestHost(TEditor editor, double width = 480, double height = 200)
    {
        Editor = editor;
        Sink = new Button { Content = "sink" };
        Window = new Window
        {
            Width = width,
            Height = height,
            Content = new StackPanel { Children = { editor, Sink } },
        };
        Window.Show();
        HeadlessTestHelpers.Settle();
    }

    public void Dispose()
    {
        Window.Close();
        HeadlessTestHelpers.Settle();
    }

    public TEditor Editor { get; }

    public Window Window { get; }

    public Button Sink { get; }

    public TextBox FirstTextBox => RequireDescendant<TextBox>();

    public T Require<T>(string name)
        where T : Control
    {
        T? found = FindNamed<T>(Editor, name);
        Assert.That(found, Is.Not.Null, $"Template part '{name}' was not found.");
        return found!;
    }

    public T RequireDescendant<T>()
        where T : Control
    {
        T? found = HeadlessTestHelpers.FindDescendant<T>(Editor);
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

    private static T? FindNamed<T>(Visual root, string name)
        where T : Control
    {
        foreach (Visual child in root.GetVisualChildren())
        {
            if (child is T match && match.Name == name)
            {
                return match;
            }

            T? found = FindNamed<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

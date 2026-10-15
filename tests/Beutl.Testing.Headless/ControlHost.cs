using Avalonia.Controls;

namespace Beutl.Testing.Headless;

// Shows a headless window and settles the dispatcher, and closes the window when disposed. A test that has to
// clean up before the window closes does that in a finally inside the host's using scope.
public sealed class ControlHost : IDisposable
{
    public ControlHost(Control content, double width = 320, double height = 200)
        : this(new Window { Width = width, Height = height, Content = content })
    {
    }

    public ControlHost(Window window)
    {
        Window = window;
        Window.Show();
        HeadlessTestHelpers.Settle();
    }

    public Window Window { get; }

    public void Dispose()
    {
        Window.Close();
        HeadlessTestHelpers.Settle();
    }
}

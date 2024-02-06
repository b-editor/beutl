using Beutl.Threading;

namespace Beutl.Rendering;

public static class RenderThread
{
    public static Dispatcher Dispatcher { get; } = Dispatcher.Spawn(() =>
    {
        Thread.CurrentThread.Name = "Beutl.RenderThread";
        Thread.CurrentThread.IsBackground = true;
    });
}

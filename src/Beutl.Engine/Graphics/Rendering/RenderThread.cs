using System.Reactive.Concurrency;
using Beutl.Engine.Threading;
using Beutl.Threading;

namespace Beutl.Graphics.Rendering;

public static class RenderThread
{
    public static Dispatcher Dispatcher { get; } = Dispatcher.Spawn(() =>
    {
        Thread.CurrentThread.Name = "Beutl.RenderThread";
        Thread.CurrentThread.IsBackground = true;
    });

    public static IScheduler Scheduler { get; } = new DispatcherLocalScheduler(Dispatcher);
}

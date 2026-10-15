using Beutl.Threading;

namespace Beutl.Audio.Composing;

public static class ComposeThread
{
    public static Dispatcher Dispatcher { get; } = Dispatcher.Spawn(() =>
    {
        Thread.CurrentThread.Name = "Beutl.ComposeThread";
        Thread.CurrentThread.IsBackground = true;
    });
}

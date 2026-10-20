using Beutl.Graphics.Rendering;
using Beutl.Testing.Headless;
using Beutl.Threading;

namespace Beutl.HeadlessUITests;

// Runs the UI jobs that hand work to the render thread, waits until the render thread reaches a no-op queued at the
// given priority, so the work queued ahead of it has run, and then runs the UI jobs it queued back. The wait is
// bounded, so a render-thread deadlock fails the test with a stack trace instead of hanging the run.
internal static class RenderThreadDrain
{
    public static void Drain(TimeSpan timeout, DispatchPriority priority = DispatchPriority.Medium)
    {
        HeadlessTestHelpers.Settle();
        using var cancellation = new CancellationTokenSource(timeout);
        RenderThread.Dispatcher.Invoke(static () => { }, priority, cancellation.Token);
        HeadlessTestHelpers.Settle();
    }
}

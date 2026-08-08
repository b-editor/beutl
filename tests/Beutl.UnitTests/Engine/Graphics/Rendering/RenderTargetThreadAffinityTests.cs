using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Skia's GPU context is thread-affine: releasing a surface off the thread that allocated it
// corrupts the context and faults the render thread later (SIGSEGV inside libSkiaSharp with no
// managed stack). These lock the hop that keeps the release on the owning thread.
public class RenderTargetThreadAffinityTests
{
    [Test]
    public void Dispose_from_another_thread_releases_on_the_owning_thread()
    {
        RenderTarget target = RenderThread.Dispatcher.Invoke(() => RenderTarget.CreateNull(4, 4));
        using var occupied = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        RenderThread.Dispatcher.Dispatch(() =>
        {
            occupied.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        });
        Assert.That(occupied.Wait(TimeSpan.FromSeconds(30)), Is.True, "the render thread never picked up the blocker");

        Task dispose = Task.Run(target.Dispose);

        Assert.That(dispose.Wait(TimeSpan.FromMilliseconds(500)), Is.False,
            "Dispose returned while the render thread was busy, so it released the surface on the calling thread.");

        release.Set();
        Assert.That(dispose.Wait(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(target.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_on_the_owning_thread_releases_inline()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            RenderTarget target = RenderTarget.CreateNull(4, 4);

            target.Dispose();

            Assert.That(target.IsDisposed, Is.True);
        });
    }
}

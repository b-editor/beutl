using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Skia's GPU context is thread-affine: releasing a surface off the thread that allocated it
// corrupts the context and faults the render thread later (SIGSEGV inside libSkiaSharp with no
// managed stack). These assert on the backing SKSurface rather than on IsDisposed, which Dispose
// sets before the release runs and so cannot distinguish an inline release from a queued one.
public class RenderTargetThreadAffinityTests
{
    private sealed class ProbeRenderTarget(SKSurface surface) : RenderTarget(surface, 4, 4);

    private static ProbeRenderTarget CreateOnRenderThread(out SKSurface surface)
    {
        SKSurface? created = null;
        ProbeRenderTarget target = RenderThread.Dispatcher.Invoke(() =>
        {
            created = SKSurface.CreateNull(4, 4);
            return new ProbeRenderTarget(created);
        });
        surface = created!;
        return target;
    }

    [Test]
    public void Dispose_from_another_thread_defers_the_release_to_the_owning_thread()
    {
        ProbeRenderTarget target = CreateOnRenderThread(out SKSurface surface);
        using var occupied = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var disposer = new Thread(() =>
        {
            entered.Set();
            target.Dispose();
        })
        { IsBackground = true, Name = "dispose-probe" };

        try
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                occupied.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            });
            Assert.That(occupied.Wait(TimeSpan.FromSeconds(30)), Is.True, "the render thread never took the blocker");

            // A dedicated thread rather than the pool: a starved pool could delay Dispose past the
            // observation below and let an inline release pass unnoticed.
            disposer.Start();
            Assert.That(entered.Wait(TimeSpan.FromSeconds(30)), Is.True);
            WaitUntilBlocked(disposer);

            Assert.That(surface.Handle, Is.Not.EqualTo(IntPtr.Zero),
                "the surface was released while the render thread was blocked, so Dispose released it on the calling thread");
        }
        finally
        {
            release.Set();
        }

        Assert.That(disposer.Join(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(surface.Handle, Is.EqualTo(IntPtr.Zero), "the surface should be released once the render thread is free");
        Assert.That(target.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_on_the_owning_thread_releases_inline()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            SKSurface surface = SKSurface.CreateNull(4, 4);
            var target = new ProbeRenderTarget(surface);

            target.Dispose();

            Assert.That(surface.Handle, Is.EqualTo(IntPtr.Zero),
                "disposal on the owning thread must release the surface before it returns, not queue it");
            Assert.That(target.IsDisposed, Is.True);
        });
    }

    private static void WaitUntilBlocked(Thread thread)
    {
        for (int i = 0; i < 300 && (thread.ThreadState & ThreadState.WaitSleepJoin) == 0; i++)
        {
            Thread.Sleep(10);
        }
    }
}

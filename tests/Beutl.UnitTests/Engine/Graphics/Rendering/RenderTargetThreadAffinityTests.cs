using Beutl.Graphics;
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
            Assert.That(WaitUntilBlocked(disposer), Is.True,
                "Dispose never blocked while the owning render thread was occupied");

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

    // Slow is not stopped: a dispatcher that has not drained the release yet may be mid-frame and
    // still using what the release would tear down, so waiting must time out into leaving the work
    // queued, never into releasing here.
    [Test]
    public void Dispose_gives_up_waiting_rather_than_releasing_off_a_busy_owning_thread()
    {
        ProbeRenderTarget target = CreateOnRenderThread(out SKSurface surface);
        using var occupied = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        try
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                occupied.Set();
                release.Wait(TimeSpan.FromSeconds(60));
            });
            Assert.That(occupied.Wait(TimeSpan.FromSeconds(30)), Is.True, "the render thread never took the blocker");

            Task dispose = Task.Run(target.Dispose);

            Assert.That(dispose.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "Dispose must stop waiting on a busy dispatcher instead of blocking its caller indefinitely");
            Assert.That(surface.Handle, Is.Not.EqualTo(IntPtr.Zero),
                "giving up must leave the release queued, not run it on the calling thread while the render thread is live");
        }
        finally
        {
            release.Set();
        }

        Assert.That(WaitUntilReleased(surface), Is.True,
            "the queued release should still run once the render thread drains it");
    }

    // Giving up leaves the cleanup queued with IsDisposed still false, so the second Dispose has to
    // be turned away by something claimed before the queue, or the shared paints get disposed twice.
    [Test]
    public void Repeated_canvas_dispose_behind_a_busy_owning_thread_queues_one_cleanup()
    {
        ImmediateCanvas canvas = RenderThread.Dispatcher.Invoke(() =>
            new ImmediateCanvas(RenderTarget.CreateNull(4, 4), 1f, 1f, new Size(4, 4)));
        using var occupied = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        try
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                occupied.Set();
                release.Wait(TimeSpan.FromSeconds(60));
            });
            Assert.That(occupied.Wait(TimeSpan.FromSeconds(30)), Is.True, "the render thread never took the blocker");

            Assert.That(Task.Run(canvas.Dispose).Wait(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(canvas.IsDisposed, Is.False, "precondition: the first Dispose left the cleanup queued");

            // A guard on IsDisposed alone would queue a rival cleanup and block for the full deadline.
            Assert.That(Task.Run(canvas.Dispose).Wait(TimeSpan.FromSeconds(1)), Is.True,
                "a second Dispose must be turned away immediately, not queue another cleanup");
        }
        finally
        {
            release.Set();
        }

        RenderThread.Dispatcher.Invoke(() => { });
        Assert.That(canvas.IsDisposed, Is.True);
    }

    // The deadline exists for a release that never starts. Once it is running on the owner thread,
    // returning early would report free resources that are still being torn down.
    [Test]
    public void A_release_slower_than_the_deadline_is_waited_out_once_it_has_started()
    {
        using var finish = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        var completed = false;

        Task caller = Task.Run(() => GpuResourceRelease.Run(RenderThread.Dispatcher, () =>
        {
            started.Set();
            finish.Wait(TimeSpan.FromSeconds(60));
            completed = true;
        }));

        try
        {
            // A caller the pool never scheduled satisfies the assertion below just as well.
            Assert.That(started.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the render thread never started the release");

            // Under the finally: a failure would otherwise leave the shared render dispatcher
            // blocked in the callback and take out every later test.
            Assert.That(caller.Wait(TimeSpan.FromSeconds(8)), Is.False,
                "Run returned while the release it started was still executing");
        }
        finally
        {
            finish.Set();
        }

        Assert.That(caller.Wait(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(completed, Is.True);
    }

    [Test]
    public void A_release_that_throws_after_the_wait_was_given_up_leaves_the_dispatcher_usable()
    {
        using var occupied = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        try
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                occupied.Set();
                release.Wait(TimeSpan.FromSeconds(60));
            });
            Assert.That(occupied.Wait(TimeSpan.FromSeconds(30)), Is.True, "the render thread never took the blocker");

            Assert.DoesNotThrow(() => GpuResourceRelease.Run(
                RenderThread.Dispatcher,
                static () => throw new InvalidOperationException("release failed after the caller gave up")));
        }
        finally
        {
            release.Set();
        }

        Assert.That(RenderThread.Dispatcher.InvokeAsync(static () => { }).Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the render thread must keep draining after a queued release faulted");
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

    private static bool WaitUntilReleased(SKSurface surface)
    {
        for (int i = 0; i < 300; i++)
        {
            if (surface.Handle == IntPtr.Zero)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static bool WaitUntilBlocked(Thread thread)
    {
        for (int i = 0; i < 300; i++)
        {
            if ((thread.ThreadState & ThreadState.WaitSleepJoin) != 0)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }
}

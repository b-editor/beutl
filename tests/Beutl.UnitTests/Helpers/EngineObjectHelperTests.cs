using System.Reactive.Linq;
using System.Reactive.Subjects;

using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Threading;

namespace Beutl.UnitTests.Helpers;

[TestFixture]
public class EngineObjectHelperTests
{
    // The subscription creates and updates its resource inside a posted render-thread callback, and the
    // render thread installs no unhandled-exception handler, so an escaping exception unwinds its loop
    // and every later render on that thread is lost.
    [Test]
    public void A_failing_resource_factory_reports_through_the_observer_and_spares_the_render_thread()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        var failure = new InvalidOperationException("the resource factory rejected the current state");
        Exception? reported = null;
        using var reportedSignal = new ManualResetEventSlim();

        using (probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       time,
                       (_, _) => throw failure)
                   .Subscribe(
                       _ => { },
                       ex =>
                       {
                           reported = ex;
                           reportedSignal.Set();
                       }))
        {
            Assert.That(reportedSignal.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the failure never reached the observer");
        }

        Assert.That(reported, Is.SameAs(failure));

        using var stillAlive = new ManualResetEventSlim();
        RenderThread.Dispatcher.Dispatch(stillAlive.Set);
        Assert.That(stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the render thread stopped taking work after the failed callback");
    }

    // Teardown releases the resource from a posted render-thread callback, where a throwing Dispose
    // would unwind the loop just as a throwing factory would.
    [Test]
    public void A_throwing_resource_dispose_does_not_take_the_render_thread_down()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();

        IDisposable subscription = probe
            .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                time,
                (_, _) => new ThrowingResource())
            .Subscribe(_ => published.Set());
        Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

        subscription.Dispose();

        using var stillAlive = new ManualResetEventSlim();
        RenderThread.Dispatcher.Dispatch(stillAlive.Set);
        Assert.That(stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the render thread stopped taking work after the failed teardown");
    }

    // Rx's default error handler rethrows on the source thread, so the trigger's failure would never
    // reach the subscriber and the resource would stay held.
    [Test]
    public void A_failing_time_stream_reaches_the_observer()
    {
        var probe = new ProbeObject();
        var time = new Subject<TimeSpan>();
        var failure = new InvalidOperationException("the clock faulted");
        Exception? reported = null;

        using (probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       time,
                       (o, c) => o.ToResource(c))
                   .Subscribe(_ => { }, ex => reported = ex))
        {
            time.OnError(failure);
        }

        Assert.That(reported, Is.SameAs(failure));
    }

    // The release is only ever queued to the render dispatcher, and a dispatcher stops draining its queue
    // the moment a shutdown begins. A shutdown starting after that dispatch - or before the queued call is
    // reached - therefore abandoned the release, leaving the resource held and the token source alive with
    // the subscription already gone and nothing left to notice.
    [Test]
    public void Render_dispatcher_shutdown_releases_a_resource_whose_dispatch_it_abandoned()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        CountingResource? created = null;

        try
        {
            IDisposable subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    time,
                    (_, _) => new CountingResource(),
                    dispatcher)
                .Subscribe(t =>
                {
                    created = t.Resource;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            dispatcher.Dispatch(
                () =>
                {
                    blockerEntered.Set();
                    releaseBlocker.Wait();
                },
                DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(30)), Is.True);

            subscription.Dispose();
            Assert.That(created!.IsDisposed, Is.False, "the release is queued behind the blocked operation");

            dispatcher.Shutdown();

            Assert.Multiple(() =>
            {
                Assert.That(created.IsDisposed, Is.True);
                Assert.That(created.DisposeCalls, Is.EqualTo(1));
            });

            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(
                created.DisposeCalls, Is.EqualTo(1),
                "draining the queue must not release the resource a second time");
        }
        finally
        {
            releaseBlocker.Set();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            created?.Dispose();
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class ProbeObject : EngineObject;

    private sealed class ThrowingResource : EngineObject.Resource
    {
        // The base finalizer calls Dispose(false); throwing from there would kill the process rather
        // than exercise the explicit-release path under test.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                throw new InvalidOperationException("this resource refuses to be released");
        }
    }

    private sealed class CountingResource : EngineObject.Resource
    {
        private int _disposeCalls;

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        // The base finalizer also routes here; only the explicit release is under test.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Interlocked.Increment(ref _disposeCalls);
        }
    }
}

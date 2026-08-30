using System.Reactive.Linq;
using System.Reactive.Subjects;

using Beutl.Composition;
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
    //
    // Recovering it has to wait for the dispatcher thread to actually stop, not merely for Shutdown() to be
    // called: the blocked operation below stands in for a frame still reading the resource, and it keeps
    // running well past the point where HasShutdownStarted turns true.
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
                    t.Read(r => created = r);
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

            Assert.That(
                created.IsDisposed, Is.False,
                "the dispatcher thread is still inside the blocked operation");

            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(created.IsDisposed, Is.True);
                Assert.That(
                    created.DisposeCalls, Is.EqualTo(1),
                    "draining the queue must not release the resource a second time");
            });
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

    // A published resource is rebuilt in place on the render dispatcher: CompareAndUpdateList replaces the
    // entries of every list the resource owns and disposes the ones it drops. A subscriber that was handed the
    // live resource walks those lists from its own thread, so it can land midway through a rebuild - which is
    // where the path editor's "Collection was modified" and index faults come from. The resource below stands
    // in for that contract with a list it empties and refills.
    [Test]
    public void A_reader_never_observes_a_resource_midway_through_a_rebuild()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        RebuildingResource? resource = null;
        EngineResourceHandle<RebuildingResource>? handle = null;
        IDisposable? subscription = null;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, RebuildingResource>(
                    time,
                    (_, _) => resource = new RebuildingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    // Only the dispatcher thread publishes, so the rebuild's own publication cannot race the
                    // read below for this field.
                    if (published.IsSet)
                        return;

                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            // The resource now exists, so this tick takes the Update path, where the rebuild parks with the
            // item list emptied and waits for the reader below.
            time.OnNext(TimeSpan.FromSeconds(1));
            Assert.That(
                resource!.RebuildEntered.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the rebuild never started");

            int observedCount = -1;
            bool observedComplete = false;
            bool wasRead = handle!.Value.Read(r =>
            {
                observedCount = r.Items.Count;
                observedComplete = r.RebuildCompleted;
            });
            resource.ReaderArrived.Set();

            Assert.Multiple(() =>
            {
                Assert.That(wasRead, Is.True, "the handle reported itself empty");
                Assert.That(
                    observedCount, Is.EqualTo(RebuildingResource.ItemCount),
                    "the reader observed the item list midway through the rebuild");
                Assert.That(
                    observedComplete, Is.True,
                    "the reader was let in before the rebuild finished");
            });
        }
        finally
        {
            resource?.ReaderArrived.Set();
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            resource?.Dispose();
        }
    }

    // A handle is a plain value the subscriber is free to keep, and teardown disposes the resource behind it
    // from the render dispatcher. Nothing else tells the holder that happened, so the handle has to.
    [Test]
    public void A_handle_outliving_its_subscription_reads_as_empty()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        EngineResourceHandle<CountingResource>? handle = null;

        try
        {
            IDisposable subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    time,
                    (_, _) => new CountingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            subscription.Dispose();
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);

            bool wasRead = handle!.Value.Read(_ => Assert.Fail("a released resource was handed to a reader"));
            Assert.That(wasRead, Is.False);
        }
        finally
        {
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
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

    private sealed class RebuildingResource : EngineObject.Resource
    {
        public const int ItemCount = 8;

        public RebuildingResource()
        {
            Fill();
        }

        public List<int> Items { get; } = [];

        public bool RebuildCompleted { get; private set; }

        public ManualResetEventSlim RebuildEntered { get; } = new();

        public ManualResetEventSlim ReaderArrived { get; } = new();

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            RebuildCompleted = false;
            Items.Clear();
            RebuildEntered.Set();

            // Bounded so the fixed hand-off, which holds the reader off until this returns, cannot deadlock.
            // The wait only decides how long that case takes; the verdict comes from what the reader saw.
            ReaderArrived.Wait(TimeSpan.FromSeconds(2));

            Fill();
            RebuildCompleted = true;
            Version++;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RebuildEntered.Dispose();
                ReaderArrived.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Fill()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                Items.Add(i);
            }
        }
    }
}

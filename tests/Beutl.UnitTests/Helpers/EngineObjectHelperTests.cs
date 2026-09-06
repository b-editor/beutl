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

    // Every production subscriber reaches this through a Subscribe overload that installs no error handler,
    // and Rx's default one rethrows the failure from inside OnError. The report runs inline on the
    // dispatcher callback that raised it, past the catch that would have contained it, so that rethrow
    // escapes into the loop - which installs no unhandled-exception handler and dies with it.
    [Test]
    public void A_failure_reported_to_a_subscriber_without_an_error_handler_spares_the_dispatcher()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var attempted = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        // An unwound loop rethrows off the dispatcher's own thread, which would end the test host rather
        // than the test; catching it there leaves the dead loop to be observed instead.
        dispatcher._catchExceptions = true;
        IDisposable? subscription = null;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                    time,
                    (_, _) =>
                    {
                        attempted.Set();
                        throw new InvalidOperationException("the resource factory rejected the current state");
                    },
                    dispatcher)
                .Subscribe(_ => { });

            Assert.That(attempted.Wait(TimeSpan.FromSeconds(30)), Is.True, "the resource was never built");

            using var stillAlive = new ManualResetEventSlim();
            dispatcher.Dispatch(stillAlive.Set);
            Assert.That(
                stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the dispatcher stopped taking work after reporting the failure");
        }
        finally
        {
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
        }
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

    // Project hands back a handle onto a child the parent owns, and the parent's rebuild disposes that child
    // the moment CompareAndUpdateObject or CompareAndUpdateList replaces or drops it. The gate only records
    // the subscription's final release, so a projection that tracked nothing else stayed "live" and lent out
    // a resource that had already been released - Geometry.Resource answers a read of one with
    // ObjectDisposedException, and the generated PostDispose overrides have freed its native handles by then.
    [Test]
    public void A_projected_handle_reads_as_empty_once_its_child_is_replaced()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var projected = new ManualResetEventSlim();
        using var replaced = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        ReplacingParentResource? parent = null;
        EngineResourceHandle<CountingResource>? staleChild = null;
        CountingResource? firstChild = null;
        IDisposable? subscription = null;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, ReplacingParentResource>(
                    time,
                    (_, _) => parent = new ReplacingParentResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    // Only the dispatcher thread publishes, so the rebuild's own publication cannot race the
                    // projection below for these fields.
                    if (!projected.IsSet)
                    {
                        staleChild = h.Project(r => r.Child);
                        staleChild!.Value.Read(c => firstChild = c);
                        projected.Set();
                        return;
                    }

                    replaced.Set();
                });

            Assert.That(projected.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            // The resource now exists, so this tick takes the Update path, which drops the child the
            // projection above points at and installs a fresh one in its place.
            time.OnNext(TimeSpan.FromSeconds(1));
            Assert.That(replaced.Wait(TimeSpan.FromSeconds(30)), Is.True, "the replacement was never published");
            Assert.That(firstChild!.IsDisposed, Is.True, "the rebuild kept the original child");

            bool wasRead = staleChild!.Value.Read(_ => Assert.Fail("a released child was handed to a reader"));
            Assert.That(wasRead, Is.False, "the projected handle still reported itself live");
        }
        finally
        {
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            parent?.Dispose();
        }
    }

    // Most rebuilds move the parent's version without touching any one child - a shape's pen changing leaves
    // its geometry alone. Invalidating every projection on a version bump would be the cheap way to close the
    // case above, and it would cost the path editor a frame of its overlay every time anything else about the
    // shape moved, so a projection whose child survived has to keep reading.
    [Test]
    public void A_projected_handle_survives_a_rebuild_that_keeps_its_child()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var projected = new ManualResetEventSlim();
        using var rebuilt = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        RetainingParentResource? parent = null;
        EngineResourceHandle<CountingResource>? child = null;
        IDisposable? subscription = null;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, RetainingParentResource>(
                    time,
                    (_, _) => parent = new RetainingParentResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    if (!projected.IsSet)
                    {
                        child = h.Project(r => r.Child);
                        projected.Set();
                        return;
                    }

                    rebuilt.Set();
                });

            Assert.That(projected.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            time.OnNext(TimeSpan.FromSeconds(1));
            Assert.That(rebuilt.Wait(TimeSpan.FromSeconds(30)), Is.True, "the rebuild was never published");

            CountingResource? observed = null;
            bool wasRead = child!.Value.Read(c => observed = c);

            Assert.Multiple(() =>
            {
                Assert.That(wasRead, Is.True, "the projected handle reported itself empty");
                Assert.That(observed, Is.SameAs(parent!.Child));
            });
        }
        finally
        {
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            parent?.Dispose();
        }
    }

    // The editors that follow a selection bind to a source that holds nothing whenever nothing is selected.
    // A switch that merely stopped publishing there would leave the previous selection's handle standing as
    // the current value, so the empty case has to publish its own absence.
    [Test]
    public void A_source_holding_no_object_publishes_an_empty_handle()
    {
        var source = new BehaviorSubject<ProbeObject?>(null);
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        Dispatcher dispatcher = Dispatcher.Spawn();
        var published = new List<EngineResourceHandle<CountingResource>?>();

        try
        {
            using (source
                       .SwitchToEngineVersionedResource<ProbeObject, CountingResource>(
                           time,
                           (_, _) => new CountingResource(),
                           dispatcher)
                       .Subscribe(published.Add))
            {
                Assert.That(published, Has.Count.EqualTo(1));
                Assert.That(published[0], Is.Null);
            }
        }
        finally
        {
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
        }
    }

    // Moving off an object has to take its resource with it. The subscription that owns the resource is the
    // only thing holding it, and the dispatcher keeps rebuilding it on every tick for as long as that
    // subscription lives, so a switch that left it subscribed would go on paying for an object nobody is
    // looking at.
    [Test]
    public void Moving_the_source_off_an_object_publishes_an_empty_handle_and_releases_its_resource()
    {
        var probe = new ProbeObject();
        var source = new BehaviorSubject<ProbeObject?>(probe);
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        CountingResource? created = null;
        EngineResourceHandle<CountingResource>? latest = null;

        try
        {
            using (source
                       .SwitchToEngineVersionedResource<ProbeObject, CountingResource>(
                           time,
                           (_, _) => created = new CountingResource(),
                           dispatcher)
                       .Subscribe(h =>
                       {
                           latest = h;
                           if (h.HasValue)
                               published.Set();
                       }))
            {
                Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

                source.OnNext(null);

                Assert.That(latest, Is.Null, "the empty source kept publishing the previous object's handle");
                Assert.That(
                    () => created!.IsDisposed, Is.True.After(30_000, 50),
                    "the resource outlived the object the source moved off");
            }
        }
        finally
        {
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            created?.Dispose();
        }
    }

    // A dispatcher runs a cleanup requested from its own thread inline rather than queueing it, and the lock
    // the gate hands readers is re-entrant, so a subscriber that disposes its subscription from inside a read
    // on the owning dispatcher walks straight back into the gate it is already holding. Releasing there frees
    // the resource the reader above still has in hand - the read goes on running against a disposed resource,
    // which is exactly what the handle exists to prevent.
    [Test]
    public void Disposing_the_subscription_from_inside_a_read_leaves_the_resource_alive_until_the_read_ends()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        using var readFinished = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        EngineResourceHandle<CountingResource>? handle = null;
        CountingResource? created = null;
        IDisposable? subscription = null;
        bool wasRead = false;
        bool disposedOnEntry = true;
        bool disposedAcrossTheRelease = true;
        bool disposedOnceTheReadEnded = false;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    time,
                    (_, _) => created = new CountingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    // Only the dispatcher thread publishes, so a later rebuild cannot race the read below
                    // for this field.
                    if (published.IsSet)
                        return;

                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            // On the dispatcher's own thread, which is what makes the release run inline instead of queueing.
            dispatcher.Dispatch(() =>
            {
                wasRead = handle!.Value.Read(r =>
                {
                    disposedOnEntry = r.IsDisposed;
                    subscription!.Dispose();
                    disposedAcrossTheRelease = r.IsDisposed;
                });

                disposedOnceTheReadEnded = created!.IsDisposed;
                readFinished.Set();
            });

            Assert.That(readFinished.Wait(TimeSpan.FromSeconds(30)), Is.True, "the read never finished");

            Assert.Multiple(() =>
            {
                Assert.That(wasRead, Is.True, "the handle reported itself empty");
                Assert.That(disposedOnEntry, Is.False, "the resource was already gone when the read started");
                Assert.That(
                    disposedAcrossTheRelease, Is.False,
                    "the release ran under the reader and disposed the resource it was still holding");
                Assert.That(
                    disposedOnceTheReadEnded, Is.True,
                    "the reader left without running the release it had held off");
                Assert.That(
                    created!.DisposeCalls, Is.EqualTo(1),
                    "the deferred release ran on top of one that had already happened");
            });
        }
        finally
        {
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            created?.Dispose();
        }
    }

    // The control for the case above: off the owning dispatcher the release is queued rather than run inline,
    // so it reaches the gate as an ordinary contender and blocks on the lock the reader holds. Nothing is
    // deferred here, and nothing may be disposed early either - which is what pins the fix to the re-entrant
    // path rather than to releases in general.
    [Test]
    public void Disposing_the_subscription_off_the_dispatcher_waits_for_the_reader_at_the_gate()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        EngineResourceHandle<CountingResource>? handle = null;
        CountingResource? created = null;
        IDisposable? subscription = null;
        bool disposeReturnedInTime = false;
        bool disposedInsideTheRead = true;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    time,
                    (_, _) => created = new CountingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    if (published.IsSet)
                        return;

                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            IDisposable toDispose = subscription;
            var disposer = new Thread(() =>
            {
                toDispose.Dispose();
                disposeReturned.Set();
            });

            // This thread is neither the dispatcher's nor the disposer's, so the read below holds the gate
            // while the queued release waits for it.
            bool wasRead = handle!.Value.Read(r =>
            {
                disposer.Start();
                disposeReturnedInTime = disposeReturned.Wait(TimeSpan.FromSeconds(30));
                disposedInsideTheRead = r.IsDisposed;
            });
            Assert.That(disposer.Join(TimeSpan.FromSeconds(30)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(wasRead, Is.True, "the handle reported itself empty");
                Assert.That(disposeReturnedInTime, Is.True, "the off-thread dispose never returned");
                Assert.That(
                    disposedInsideTheRead, Is.False,
                    "the queued release reached the resource while a reader was holding the gate");
            });

            Assert.That(
                () => created!.IsDisposed, Is.True.After(30_000, 50),
                "the queued release never reached the resource");
        }
        finally
        {
            subscription?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            created?.Dispose();
        }
    }

    // The deferred release runs from the reader's own stack, long after ReleaseResource's try/catch has
    // returned, so nothing upstream is left to contain a throwing Dispose. On the render thread that would
    // unwind the loop - the same hazard A_throwing_resource_dispose_does_not_take_the_render_thread_down
    // pins on the non-deferred path - and it would strike inside a reader that did nothing wrong.
    //
    // Resource.Dispose() sets IsDisposed only after Dispose(true) returns, so a resource whose release threw
    // stays undisposed. The evidence that the deferred release actually ran is the call count, not the flag.
    [Test]
    public void A_throwing_dispose_deferred_behind_a_reader_stays_out_of_the_read()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        using var published = new ManualResetEventSlim();
        using var readFinished = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        EngineResourceHandle<ThrowingCountingResource>? handle = null;
        ThrowingCountingResource? created = null;
        IDisposable? subscription = null;
        Exception? escaped = null;
        bool wasRead = false;

        try
        {
            subscription = probe
                .SubscribeEngineVersionedResource<ProbeObject, ThrowingCountingResource>(
                    time,
                    (_, _) => created = new ThrowingCountingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    if (published.IsSet)
                        return;

                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            // On the dispatcher's own thread, so the release runs inline and defers to this reader.
            dispatcher.Dispatch(() =>
            {
                try
                {
                    wasRead = handle!.Value.Read(_ => subscription!.Dispose());
                }
                catch (Exception ex)
                {
                    // Caught rather than left to unwind: an escaping exception would take the dispatcher's
                    // loop with it and the assertions below would never be reached.
                    escaped = ex;
                }

                readFinished.Set();
            });

            Assert.That(readFinished.Wait(TimeSpan.FromSeconds(30)), Is.True, "the read never finished");

            Assert.Multiple(() =>
            {
                Assert.That(escaped, Is.Null, "the deferred release threw into the reader");
                Assert.That(wasRead, Is.True, "the handle reported itself empty");
                Assert.That(
                    created!.DisposeCalls, Is.EqualTo(1),
                    "the deferred release never reached the resource");
                Assert.That(
                    created.IsDisposed, Is.False,
                    "Resource.Dispose() sets IsDisposed only after Dispose(true) returns");
            });

            using var stillAlive = new ManualResetEventSlim();
            dispatcher.Dispatch(stillAlive.Set);
            Assert.That(
                stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the dispatcher stopped taking work after the deferred release");
        }
        finally
        {
            subscription?.Dispose();
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

    // Both halves of the throwing case at once. The call count is the only trace a deferred release leaves,
    // because Resource.Dispose() never reaches IsDisposed when Dispose(true) throws.
    private sealed class ThrowingCountingResource : EngineObject.Resource
    {
        private int _disposeCalls;

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        // The base finalizer routes here too, and it does run for this resource: a throwing Dispose never
        // reaches GC.SuppressFinalize. Only the explicit release may throw.
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            Interlocked.Increment(ref _disposeCalls);
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

    // Stands in for CompareAndUpdateObject's replace path: the dropped child is disposed and a fresh one takes
    // its place, all inside the Update the subscription runs under its gate.
    private sealed class ReplacingParentResource : EngineObject.Resource
    {
        public CountingResource Child { get; private set; } = new();

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            CountingResource dropped = Child;
            Child = new CountingResource();
            Version++;
            dropped.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Child.Dispose();

            base.Dispose(disposing);
        }
    }

    // The other half of that contract: a rebuild that moves the parent's version while the child it owns
    // stays exactly where it was.
    private sealed class RetainingParentResource : EngineObject.Resource
    {
        public CountingResource Child { get; } = new();

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            Version++;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Child.Dispose();

            base.Dispose(disposing);
        }
    }
}

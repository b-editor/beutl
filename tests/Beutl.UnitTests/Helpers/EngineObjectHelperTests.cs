using System.Reactive.Linq;
using System.Reactive.Subjects;

using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Threading;
using Beutl.UnitTests.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Helpers;

[TestFixture]
public class EngineObjectHelperTests
{
    private ProbeObject _probe = null!;
    private BehaviorSubject<MediaTime> _time = null!;

    [SetUp]
    public void SetUp()
    {
        _probe = new ProbeObject();
        _time = new BehaviorSubject<MediaTime>(MediaTime.Zero);
    }

    // The subscription creates and updates its resource inside a posted render-thread callback, and the
    // render thread installs no unhandled-exception handler, so an escaping exception unwinds its loop
    // and every later render on that thread is lost.
    [Test]
    public void A_failing_resource_factory_reports_through_the_observer_and_spares_the_render_thread()
    {
        var failure = new InvalidOperationException("the resource factory rejected the current state");
        Exception? reported = null;
        using var reportedSignal = new ManualResetEventSlim();

        using (_probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       _time,
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

    // Production subscribers install no error handler, and Rx's default one rethrows from inside OnError, inline on
    // the dispatcher callback and past its catch, into a loop that installs no unhandled-exception handler.
    [Test]
    public void A_failure_reported_to_a_subscriber_without_an_error_handler_spares_the_dispatcher()
    {
        using var attempted = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        // An unwound loop rethrows off the dispatcher's own thread, which would end the test host rather
        // than the test; catching it there leaves the dead loop to be observed instead.
        dispatcher._catchExceptions = true;
        IDisposable? subscription = null;

        try
        {
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                    _time,
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
            ShutDownAndJoin(dispatcher);
        }
    }

    // Teardown releases the resource from a posted render-thread callback, where a throwing Dispose
    // would unwind the loop just as a throwing factory would.
    [Test]
    public void A_throwing_resource_dispose_does_not_take_the_render_thread_down()
    {
        using var published = new ManualResetEventSlim();

        IDisposable subscription = _probe
            .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                _time,
                (_, _) => new ThrowingResource())
            .Subscribe(_ => published.Set());
        Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

        subscription.Dispose();

        using var stillAlive = new ManualResetEventSlim();
        RenderThread.Dispatcher.Dispatch(stillAlive.Set);
        Assert.That(stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the render thread stopped taking work after the failed teardown");
    }

    // A failed update releases the resource; when that release fails too, the failure is logged and the
    // update's own failure still reaches the observer.
    [Test]
    [NonParallelizable]
    public void A_release_failing_after_a_failed_update_is_logged()
    {
        using var logs = new CapturingLoggerProvider(category => category == typeof(EngineObjectHelper).FullName);
        Log.LoggerFactory.AddProvider(logs);
        Exception? reported = null;
        using var published = new ManualResetEventSlim();
        using var reportedSignal = new ManualResetEventSlim();

        using (_probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       _time,
                       (_, _) => new FailingUpdateResource())
                   .Subscribe(
                       _ => published.Set(),
                       ex =>
                       {
                           reported = ex;
                           reportedSignal.Set();
                       }))
        {
            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");
            _time.OnNext(MediaTime.FromSeconds(1));
            Assert.That(reportedSignal.Wait(TimeSpan.FromSeconds(30)), Is.True, "the failed update was not reported");
        }

        Assert.That(reported?.Message, Is.EqualTo("the update failed"));
        Assert.That(reported!.Data, Is.Empty);
        Assert.That(logs.Entries.Count(entry => entry is (LogLevel.Warning, { Message: FailingUpdateResource.ReleaseFailure })),
            Is.EqualTo(1));
    }

    // Rx's default error handler rethrows on the source thread, so the trigger's failure would never
    // reach the subscriber and the resource would stay held.
    [Test]
    public void A_failing_time_stream_reaches_the_observer()
    {
        var time = new Subject<MediaTime>();
        var failure = new InvalidOperationException("the clock faulted");
        Exception? reported = null;

        using (_probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       time,
                       (o, c) => o.ToResource(c))
                   .Subscribe(_ => { }, ex => reported = ex))
        {
            time.OnError(failure);
        }

        Assert.That(reported, Is.SameAs(failure));
    }

    [Test]
    public void Shutdown_drops_a_release_queued_behind_a_running_operation()
    {
        using var published = new ManualResetEventSlim();
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        CountingResource? created = null;
        IDisposable? subscription = null;
        try
        {
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    _time, (_, _) => new CountingResource(), dispatcher)
                .Subscribe(handle =>
                {
                    handle.Read(resource => created = resource);
                    published.Set();
                });
            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True);
            dispatcher.Dispatch(() =>
            {
                blockerEntered.Set();
                releaseBlocker.Wait();
            }, DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(30)), Is.True);

            subscription.Dispose();
            dispatcher.Shutdown();
            Assert.That(created!.DisposeCalls, Is.Zero);
            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(created.DisposeCalls, Is.Zero);
        }
        finally
        {
            releaseBlocker.Set();
            subscription?.Dispose();
            ShutDownAndJoin(dispatcher);
            created?.Dispose();
        }
    }

    // A published resource is rebuilt in place on the render dispatcher, so a reader on another thread could land
    // midway through a rebuild, as the path editor's "Collection was modified" faults did.
    [Test]
    public void A_reader_never_observes_a_resource_midway_through_a_rebuild()
    {
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        RebuildingResource? resource = null;
        EngineResourceHandle<RebuildingResource>? handle = null;
        IDisposable? subscription = null;

        try
        {
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, RebuildingResource>(
                    _time,
                    (_, _) => resource = new RebuildingResource(),
                    dispatcher)
                .Subscribe(CaptureFirst<RebuildingResource>(published, h => handle = h));

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            // The resource now exists, so this tick takes the Reconcile path, where the rebuild parks with the
            // item list emptied and waits for the reader below.
            _time.OnNext(MediaTime.FromSeconds(1));
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
            ShutDownAndJoin(dispatcher);
            resource?.Dispose();
        }
    }

    // A handle is a plain value the subscriber is free to keep, and teardown disposes the resource behind it
    // from the render dispatcher. Nothing else tells the holder that happened, so the handle has to.
    [Test]
    public void A_handle_outliving_its_subscription_reads_as_empty()
    {
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        EngineResourceHandle<CountingResource>? handle = null;

        try
        {
            IDisposable subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    _time,
                    (_, _) => new CountingResource(),
                    dispatcher)
                .Subscribe(h =>
                {
                    handle = h;
                    published.Set();
                });

            Assert.That(published.Wait(TimeSpan.FromSeconds(30)), Is.True, "no resource was ever published");

            subscription.Dispose();
            // The teardown queues its release at low priority, so this runs after it.
            dispatcher.Invoke(static () => { }, DispatchPriority.Low);

            bool wasRead = handle!.Value.Read(_ => Assert.Fail("a released resource was handed to a reader"));
            Assert.That(wasRead, Is.False);
        }
        finally
        {
            ShutDownAndJoin(dispatcher);
        }
    }

    // Project hands back a handle onto a child the parent owns, and the parent's rebuild disposes that child when it
    // replaces or drops it, so the projected handle must read as empty rather than lend out the released child.
    [Test]
    public void A_projected_handle_reads_as_empty_once_its_child_is_replaced()
    {
        using var projected = new ManualResetEventSlim();
        using var replaced = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        ReplacingParentResource? parent = null;
        EngineResourceHandle<CountingResource>? staleChild = null;
        CountingResource? firstChild = null;
        IDisposable? subscription = null;

        try
        {
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, ReplacingParentResource>(
                    _time,
                    (_, _) => parent = new ReplacingParentResource(),
                    dispatcher)
                .Subscribe(h =>
                {
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

            // The resource now exists, so this tick takes the Reconcile path, which drops the child the
            // projection above points at and installs a fresh one in its place.
            _time.OnNext(MediaTime.FromSeconds(1));
            Assert.That(replaced.Wait(TimeSpan.FromSeconds(30)), Is.True, "the replacement was never published");
            Assert.That(firstChild!.IsDisposed, Is.True, "the rebuild kept the original child");

            bool wasRead = staleChild!.Value.Read(_ => Assert.Fail("a released child was handed to a reader"));
            Assert.That(wasRead, Is.False, "the projected handle still reported itself live");
        }
        finally
        {
            subscription?.Dispose();
            ShutDownAndJoin(dispatcher);
            parent?.Dispose();
        }
    }

    // Most rebuilds bump the parent's version without touching the child, and invalidating every projection would cost
    // the path editor a frame of its overlay each time, so a projection whose child survived has to keep reading.
    [Test]
    public void A_projected_handle_survives_a_rebuild_that_keeps_its_child()
    {
        using var projected = new ManualResetEventSlim();
        using var rebuilt = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        RetainingParentResource? parent = null;
        EngineResourceHandle<CountingResource>? child = null;
        IDisposable? subscription = null;

        try
        {
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, RetainingParentResource>(
                    _time,
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

            _time.OnNext(MediaTime.FromSeconds(1));
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
            ShutDownAndJoin(dispatcher);
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
        Dispatcher dispatcher = Dispatcher.Spawn();
        var published = new List<EngineResourceHandle<CountingResource>?>();

        try
        {
            using (source
                       .SwitchToEngineVersionedResource<ProbeObject, CountingResource>(
                           _time,
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
            ShutDownAndJoin(dispatcher);
        }
    }

    // Moving off an object has to release its resource: the dispatcher keeps rebuilding it on every tick for as long as
    // the owning subscription lives.
    [Test]
    public void Moving_the_source_off_an_object_publishes_an_empty_handle_and_releases_its_resource()
    {
        var source = new BehaviorSubject<ProbeObject?>(_probe);
        using var published = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        CountingResource? created = null;
        EngineResourceHandle<CountingResource>? latest = null;

        try
        {
            using (source
                       .SwitchToEngineVersionedResource<ProbeObject, CountingResource>(
                           _time,
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
            ShutDownAndJoin(dispatcher);
            created?.Dispose();
        }
    }

    // On its own thread a dispatcher runs the cleanup inline and the gate's lock is re-entrant, so disposing from
    // inside a read must defer the release until the read ends rather than free the resource the reader still holds.
    [Test]
    public void Disposing_the_subscription_from_inside_a_read_leaves_the_resource_alive_until_the_read_ends()
    {
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
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    _time,
                    (_, _) => created = new CountingResource(),
                    dispatcher)
                .Subscribe(CaptureFirst<CountingResource>(published, h => handle = h));

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
            ShutDownAndJoin(dispatcher);
            created?.Dispose();
        }
    }

    // The control for the case above: off the dispatcher the release is queued and blocks on the lock the reader holds,
    // so nothing is deferred or disposed early, which pins the fix to the re-entrant path.
    [Test]
    public void Disposing_the_subscription_off_the_dispatcher_waits_for_the_reader_at_the_gate()
    {
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
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, CountingResource>(
                    _time,
                    (_, _) => created = new CountingResource(),
                    dispatcher)
                .Subscribe(CaptureFirst<CountingResource>(published, h => handle = h));

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
            ShutDownAndJoin(dispatcher);
            created?.Dispose();
        }
    }

    // The deferred release runs from the reader's own stack, past ReleaseResource's try/catch, so a throwing Dispose
    // must be contained there rather than unwind the render loop inside a reader that did nothing wrong.
    [Test]
    public void A_throwing_dispose_deferred_behind_a_reader_stays_out_of_the_read()
    {
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
            subscription = _probe
                .SubscribeEngineVersionedResource<ProbeObject, ThrowingCountingResource>(
                    _time,
                    (_, _) => created = new ThrowingCountingResource(),
                    dispatcher)
                .Subscribe(CaptureFirst<ThrowingCountingResource>(published, h => handle = h));

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
            ShutDownAndJoin(dispatcher);
        }
    }

    private static void ShutDownAndJoin(Dispatcher dispatcher)
    {
        if (!dispatcher.HasShutdownStarted)
            dispatcher.Shutdown();
        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
    }

    // Keeps only the first handle. Only the dispatcher thread publishes, so a later rebuild's publication cannot
    // race the test's reads of the captured handle.
    private static Action<EngineResourceHandle<T>> CaptureFirst<T>(
        ManualResetEventSlim published, Action<EngineResourceHandle<T>> capture)
        where T : EngineObject.Resource
        => h =>
        {
            if (published.IsSet)
                return;

            capture(h);
            published.Set();
        };

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

    private sealed class FailingUpdateResource : EngineObject.Resource
    {
        public const string ReleaseFailure = "the resource of a failed update refuses to be released";

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
            => throw new InvalidOperationException("the update failed");

        // Only the explicit release may throw; the base finalizer also routes here.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                throw new InvalidOperationException(ReleaseFailure);
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

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

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

    // Stands in for ResourceReconciler.ReconcileChild's replace path: the dropped child is disposed and a fresh
    // one takes its place, all inside the Reconcile the subscription runs under its gate.
    private sealed class ReplacingParentResource : EngineObject.Resource
    {
        public CountingResource Child { get; private set; } = new();

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

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

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
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

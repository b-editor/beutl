using System.Threading.Channels;

using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyJobQueueTests
{
    [Test]
    public async Task EnqueueAsync_DispatchesSeriallyInArrivalOrder()
    {
        var generator = new RecordingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint first = CreateFingerprint("a.mov");
        ProxyFingerprint second = CreateFingerprint("b.mov");

        ProxyJob firstJob = await queue.EnqueueAsync(first, ProxyPreset.Quarter);
        ProxyJob secondJob = await queue.EnqueueAsync(second, ProxyPreset.Quarter);

        await generator.WaitForCountAsync(2);

        Assert.Multiple(() =>
        {
            Assert.That(generator.Sources, Is.EqualTo(new[] { first, second }));
            Assert.That(firstJob.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(secondJob.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
        });
    }

    // greptile P1: DisposeAsync must unsubscribe from the resolved generator's AvailabilityChanged, or the
    // generator keeps a live reference to the queue (leak) and can fire into a disposed object. The
    // unsubscribe now runs after the drain loop ends so a generator resolved during disposal cannot leave
    // a stray subscription the pre-drain read would miss.
    [Test]
    public async Task DisposeAsync_UnsubscribesFromGeneratorAvailability()
    {
        var generator = new ToggleAvailabilityGenerator();
        generator.SetAvailable();
        var queue = new ProxyJobQueue(generator);
        ProxyJob job = await queue.EnqueueAsync(CreateFingerprint("a.mov"), ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);
        Assume.That(generator.AvailabilityChangedSubscriberCount, Is.EqualTo(1),
            "the drain resolved the generator and subscribed to AvailabilityChanged");

        await queue.DisposeAsync();

        Assert.That(generator.AvailabilityChangedSubscriberCount, Is.EqualTo(0),
            "DisposeAsync must unsubscribe so the generator no longer references the disposed queue");
    }

    [Test]
    public async Task EnqueueAsync_DeduplicatesSourcePreset()
    {
        var generator = new BlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint source = CreateFingerprint("a.mov");

        ProxyJob first = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        generator.Release();
        await WaitForTerminalAsync(first);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(generator.StartedCount, Is.EqualTo(1), "a deduplicated enqueue must not run the generator twice");
        });
    }

    [Test]
    public async Task SkippedException_ProducesSkippedTerminalState()
    {
        await using var queue = new ProxyJobQueue(new SkippingGenerator());
        ProxyFingerprint source = CreateFingerprint("a.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Skipped));
            Assert.That(job.StatusMessage, Is.EqualTo("ineligible"));
        });
    }

    [Test]
    public async Task DisposeAsync_CancelsAndDrainsQueuedJobs()
    {
        var generator = new BlockingGenerator();
        var queue = new ProxyJobQueue(generator);
        ProxyFingerprint firstSource = CreateFingerprint("a.mov");
        ProxyFingerprint secondSource = CreateFingerprint("b.mov");

        ProxyJob first = await queue.EnqueueAsync(firstSource, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(secondSource, ProxyPreset.Quarter);

        await queue.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending(), Is.Empty);
        });
    }

    [Test]
    public async Task Cancel_DoesNotThrowAfterJobAlreadyCompleted()
    {
        var generator = new RecordingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint source = CreateFingerprint("a.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        Assert.DoesNotThrow(() => queue.Cancel(job.JobId));
        Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Succeeded), "a late Cancel must not overwrite the terminal state");
    }

    [Test]
    public async Task FailedException_WithStore_PreservesFailedEntry()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        await using var queue = new ProxyJobQueue(new FailingGenerator(), store);
        ProxyFingerprint source = CreateFingerprint("failed.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        ProxyEntry? entry = store.TryGet(source, ProxyPreset.Quarter);
        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(queue.Pending(), Is.Empty);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.State, Is.EqualTo(ProxyState.Failed));
            Assert.That(entry.FailureReason, Is.EqualTo("encode failed"));
        });
    }

    [Test]
    public async Task FailedException_WithExistingReadyEntry_KeepsReadyEntry()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyFingerprint source = CreateFingerprint("ready-before-failure.mov");
        ProxyEntry ready = CreateReadyEntry(root, source);
        store.Register(ready);
        await using var queue = new ProxyJobQueue(new FailingGenerator(), store);

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(store.TryGet(source, ProxyPreset.Quarter), Is.EqualTo(ready));
        });
    }

    [Test]
    public async Task Cancel_QueuedJobRemovesItFromPendingWithoutRunningIt()
    {
        var generator = new BlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint firstSource = CreateFingerprint("running.mov");
        ProxyFingerprint secondSource = CreateFingerprint("queued.mov");

        ProxyJob first = await queue.EnqueueAsync(firstSource, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(secondSource, ProxyPreset.Quarter);
        queue.Cancel(second.JobId);
        generator.Release();
        await WaitForTerminalAsync(first);
        await WaitForTerminalAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending(), Is.Empty);
        });
    }

    [Test]
    public async Task UnavailableGenerator_KeepsAllJobsQueuedUntilAvailabilityReturns()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new ToggleAvailabilityGenerator();
        await using var queue = new ProxyJobQueue(generator, store);
        ProxyFingerprint firstSource = CreateFingerprint("unavailable-a.mov");
        ProxyFingerprint secondSource = CreateFingerprint("unavailable-b.mov");

        ProxyJob first = await queue.EnqueueAsync(firstSource, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(secondSource, ProxyPreset.Quarter);
        await generator.UnavailableHit.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        // An unavailable generator must not fail queued work; both jobs stay Queued and no Failed
        // entry is recorded, so nothing is lost while FFmpeg is unavailable.
        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.Not.EqualTo(ProxyJobStatus.Failed));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Queued));
            Assert.That(queue.Pending(), Does.Contain(second));
            Assert.That(store.TryGet(firstSource, ProxyPreset.Quarter), Is.Null);
            Assert.That(store.TryGet(secondSource, ProxyPreset.Quarter), Is.Null);
        });

        generator.SetAvailable();
        await WaitForTerminalAsync(first);
        await WaitForTerminalAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(generator.SucceededSources, Is.EquivalentTo(new[] { firstSource, secondSource }));
        });
    }

    [Test]
    public async Task UnavailableGenerator_SelfRecoversViaBackoffWithoutAvailabilityEvent()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new ManualRecoveryGenerator();
        await using var queue = new ProxyJobQueue(
            generator,
            store,
            minUnavailableBackoff: TimeSpan.FromMilliseconds(20),
            maxUnavailableBackoff: TimeSpan.FromMilliseconds(40));
        ProxyFingerprint source = CreateFingerprint("recover.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await generator.FirstUnavailable.WaitAsync(TimeSpan.FromSeconds(5));

        // Restore availability WITHOUT firing AvailabilityChanged: only the backoff re-probe can
        // discover it. This is the transient-recovery-without-the-install-wizard path.
        generator.MakeAvailableSilently();
        await WaitForTerminalAsync(job);

        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(generator.SucceededSources, Is.EqualTo(new[] { source }));
        });
    }

    [Test]
    public async Task EnqueueAsync_HigherPriorityJumpsAheadOfEarlierQueuedBulk()
    {
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint running = CreateFingerprint("running.mov");
        ProxyFingerprint bulkA = CreateFingerprint("bulk-a.mov");
        ProxyFingerprint bulkB = CreateFingerprint("bulk-b.mov");
        ProxyFingerprint urgent = CreateFingerprint("urgent.mov");

        // Occupy the single worker so the rest sit Queued behind it.
        ProxyJob runningJob = await queue.EnqueueAsync(running, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);

        await queue.EnqueueAsync(bulkA, ProxyPreset.Quarter);
        await queue.EnqueueAsync(bulkB, ProxyPreset.Quarter);
        ProxyJob urgentJob = await queue.EnqueueAsync(urgent, ProxyPreset.Quarter, priority: 10);

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);
        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(3);
        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(4);
        generator.ReleaseAll();

        await WaitForTerminalAsync(runningJob);
        await WaitForTerminalAsync(urgentJob);

        Assert.Multiple(() =>
        {
            Assert.That(generator.StartedSources[0], Is.EqualTo(running));
            Assert.That(generator.StartedSources[1], Is.EqualTo(urgent), "the high-priority job must jump the earlier-queued bulk");
            Assert.That(generator.StartedSources.Skip(2), Is.EqualTo(new[] { bulkA, bulkB }));
        });
    }

    [Test]
    public async Task EnqueueAsync_AlreadyCanceledToken_NeverDispatchesTheJob()
    {
        // Enqueue is non-blocking (a full channel drops the wake permit, it never blocks), so there is
        // no cancel-mid-write. A token already canceled at enqueue must still be rejected before the job
        // can dispatch, and the queue must keep processing the buffered job.
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint running = CreateFingerprint("running.mov");
        ProxyFingerprint bulk = CreateFingerprint("bulk.mov");
        ProxyFingerprint canceled = CreateFingerprint("canceled.mov");

        ProxyJob runningJob = await queue.EnqueueAsync(running, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);

        await queue.EnqueueAsync(bulk, ProxyPreset.Quarter);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(
            async () => await queue.EnqueueAsync(canceled, ProxyPreset.Quarter, priority: 10, cts.Token));

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);
        generator.ReleaseAll();
        await WaitForTerminalAsync(runningJob);

        Assert.That(generator.StartedSources, Does.Not.Contain(canceled),
            "an enqueue with an already-canceled token must not dispatch its job");
    }

    [Test]
    public async Task GenerateBulk_WhileGeneratorUnavailable_DoesNotBlockEnqueue()
    {
        // Regression for the bounded-channel deadlock: the single drain loop parks on generator
        // unavailability backoff, so a large sequential bulk enqueue must still complete instead of
        // blocking on a full channel. Every job is registered as Queued, and when availability returns
        // they all run.
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new ToggleAvailabilityGenerator();
        await using var queue = new ProxyJobQueue(
            generator,
            store,
            minUnavailableBackoff: TimeSpan.FromSeconds(30),
            maxUnavailableBackoff: TimeSpan.FromSeconds(30));

        var enqueueAll = Task.Run(async () =>
        {
            var jobs = new List<ProxyJob>();
            for (int i = 0; i < 32; i++)
                jobs.Add(await queue.EnqueueAsync(CreateFingerprint($"bulk-{i}.mov"), ProxyPreset.Quarter));

            return jobs;
        });

        // Under the old bounded blocking write this hung once the queue filled while the drain parked.
        List<ProxyJob> queued = await enqueueAll.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(queued, Has.Count.EqualTo(32));

        generator.SetAvailable();
        foreach (ProxyJob job in queued)
            await WaitForTerminalAsync(job);

        Assert.That(queued.Select(static j => j.Status), Is.All.EqualTo(ProxyJobStatus.Succeeded));
    }

    [Test]
    public async Task EnqueueAsync_PriorityReachableThroughInterfaceType()
    {
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        IProxyJobQueue sut = queue;
        ProxyFingerprint running = CreateFingerprint("running.mov");
        ProxyFingerprint bulk = CreateFingerprint("bulk.mov");
        ProxyFingerprint urgent = CreateFingerprint("urgent.mov");

        ProxyJob runningJob = await sut.EnqueueAsync(running, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);

        await sut.EnqueueAsync(bulk, ProxyPreset.Quarter);
        ProxyJob urgentJob = await sut.EnqueueAsync(urgent, ProxyPreset.Quarter, priority: 10);

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);
        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(3);
        generator.ReleaseAll();

        await WaitForTerminalAsync(runningJob);
        await WaitForTerminalAsync(urgentJob);

        Assert.Multiple(() =>
        {
            Assert.That(urgentJob.Priority, Is.EqualTo(10));
            Assert.That(generator.StartedSources[0], Is.EqualTo(running));
            Assert.That(
                generator.StartedSources[1],
                Is.EqualTo(urgent),
                "priority set via IProxyJobQueue must jump the earlier-queued bulk");
            Assert.That(generator.StartedSources[2], Is.EqualTo(bulk));
        });
    }

    [Test]
    public async Task RegisterFailure_WhenStoreRegisterThrows_SurfacesSecondaryErrorNotSilently()
    {
        var store = new ThrowingRegisterStore();
        await using var queue = new ProxyJobQueue(new FailingGenerator(), store);
        ProxyFingerprint source = CreateFingerprint("bookkeeping.mov");
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.JobChanged += (_, e) =>
        {
            if (e.Kind == ProxyJobChangeKind.Failed)
                failed.TrySetResult();
        };

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(job.BookkeepingError, Is.InstanceOf<InvalidOperationException>());
        });
    }

    [Test]
    public async Task Cancel_QueuedReplacementKeepsDeduplicationAfterCanceledItemDrains()
    {
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint runningSource = CreateFingerprint("running-replacement.mov");
        ProxyFingerprint replacementSource = CreateFingerprint("queued-replacement.mov");

        ProxyJob running = await queue.EnqueueAsync(runningSource, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);
        ProxyJob canceled = await queue.EnqueueAsync(replacementSource, ProxyPreset.Quarter);
        queue.Cancel(canceled.JobId);
        ProxyJob replacement = await queue.EnqueueAsync(replacementSource, ProxyPreset.Quarter);

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);

        ProxyJob duplicate = await queue.EnqueueAsync(replacementSource, ProxyPreset.Quarter);
        generator.ReleaseAll();
        await WaitForTerminalAsync(running);
        await WaitForTerminalAsync(canceled);
        await WaitForTerminalAsync(replacement);

        Assert.Multiple(() =>
        {
            Assert.That(canceled.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(duplicate, Is.SameAs(replacement));
            Assert.That(replacement.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
        });
    }

    [Test]
    public async Task EnqueueAsync_DuplicateWithHigherPriority_PromotesQueuedJobAheadOfBulk()
    {
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint running = CreateFingerprint("running.mov");
        ProxyFingerprint bulkA = CreateFingerprint("bulk-a.mov");
        ProxyFingerprint bulkB = CreateFingerprint("bulk-b.mov");

        ProxyJob runningJob = await queue.EnqueueAsync(running, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);

        ProxyJob bulkAJob = await queue.EnqueueAsync(bulkA, ProxyPreset.Quarter);
        await queue.EnqueueAsync(bulkB, ProxyPreset.Quarter);
        ProxyJob promoted = await queue.EnqueueAsync(bulkA, ProxyPreset.Quarter, priority: 10);

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);
        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(3);
        generator.ReleaseAll();

        await WaitForTerminalAsync(runningJob);
        await WaitForTerminalAsync(bulkAJob);

        Assert.Multiple(() =>
        {
            Assert.That(promoted, Is.SameAs(bulkAJob), "a duplicate enqueue must reuse the queued job, not create a new one");
            Assert.That(bulkAJob.Priority, Is.EqualTo(10), "the queued job's priority must be promoted by the higher-priority duplicate");
            Assert.That(generator.StartedSources[0], Is.EqualTo(running));
            Assert.That(generator.StartedSources[1], Is.EqualTo(bulkA), "the promoted duplicate must jump ahead of the earlier-queued bulk");
            Assert.That(generator.StartedSources[2], Is.EqualTo(bulkB));
        });
    }

    [Test]
    public async Task EnqueueAsync_SameKeyDuringTerminalWindow_ReplacesWithoutThrowing()
    {
        var generator = new RecordingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint source = CreateFingerprint("terminal-window.mov");

        ProxyJob? replacement = null;
        Exception? reenqueueError = null;
        var reenqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.JobChanged += (_, e) =>
        {
            if (e.Kind != ProxyJobChangeKind.Succeeded || replacement != null)
                return;

            try
            {
                // Re-enqueue the same key while the just-succeeded terminal entry is still parked in
                // the queue's map (its drain loop has not run Remove yet): the duplicate-key window.
                replacement = queue.EnqueueAsync(source, ProxyPreset.Quarter).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                reenqueueError = ex;
            }
            finally
            {
                reenqueued.TrySetResult();
            }
        };

        ProxyJob first = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await reenqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(reenqueueError, Is.Null, "re-enqueue during the terminal window must not throw a duplicate-key error");
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement, Is.Not.SameAs(first), "a terminal entry must be replaced by a fresh job, not returned");
        });
    }

    [Test]
    public async Task UnavailableGenerator_WithoutAvailabilitySignal_SkipsInsteadOfRequeueingForever()
    {
        await using var queue = new ProxyJobQueue(new PermanentlyUnavailableGenerator());
        ProxyFingerprint first = CreateFingerprint("a.mov");
        ProxyFingerprint second = CreateFingerprint("b.mov");

        ProxyJob firstJob = await queue.EnqueueAsync(first, ProxyPreset.Quarter);
        ProxyJob secondJob = await queue.EnqueueAsync(second, ProxyPreset.Quarter);
        await WaitForTerminalAsync(firstJob);
        await WaitForTerminalAsync(secondJob);

        // No availability signal means the queue can never learn the generator recovered; the jobs
        // must reach a terminal Skipped state instead of requeueing forever and blocking the queue.
        Assert.Multiple(() =>
        {
            Assert.That(firstJob.Status, Is.EqualTo(ProxyJobStatus.Skipped));
            Assert.That(secondJob.Status, Is.EqualTo(ProxyJobStatus.Skipped));
            Assert.That(queue.Pending(), Is.Empty);
        });
    }

    [Test]
    public async Task UnavailableGenerator_InvalidatedMidGenerate_RequeuesUnderNextGenerator()
    {
        var replacement = new RecordingGenerator();
        ProxyJobQueue? queueRef = null;
        var swappedOut = new InvalidatingUnavailableGenerator(() => queueRef!.InvalidateGenerator());
        int providerCalls = 0;
        IProxyGenerator? Provider()
        {
            return Interlocked.Increment(ref providerCalls) == 1 ? swappedOut : replacement;
        }

        await using var queue = new ProxyJobQueue(
            (Func<IProxyGenerator?>)Provider,
            store: null,
            minUnavailableBackoff: TimeSpan.FromMilliseconds(20),
            maxUnavailableBackoff: TimeSpan.FromMilliseconds(40));
        queueRef = queue;
        ProxyFingerprint source = CreateFingerprint("swap.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        // The generator was invalidated (swapped out) while the job was mid-generate; the job must be
        // requeued and complete under the next resolved generator, not dropped as a terminal Skipped.
        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(replacement.Sources, Does.Contain(source));
        });
    }

    [Test]
    public async Task Cancel_ParkedUnavailableJob_WakesDrainLoopWithoutWaitingBackoff()
    {
        ProxyFingerprint parked = CreateFingerprint("parked.mov");
        ProxyFingerprint follower = CreateFingerprint("follower.mov");
        var generator = new SourceScopedUnavailableGenerator(parked);
        await using var queue = new ProxyJobQueue(
            generator,
            store: null,
            minUnavailableBackoff: TimeSpan.FromSeconds(30),
            maxUnavailableBackoff: TimeSpan.FromSeconds(30));

        ProxyJob parkedJob = await queue.EnqueueAsync(parked, ProxyPreset.Quarter);
        await generator.FirstUnavailable.WaitAsync(TimeSpan.FromSeconds(5));
        ProxyJob followerJob = await queue.EnqueueAsync(follower, ProxyPreset.Quarter);

        queue.Cancel(parkedJob.JobId);

        // Canceling the parked job must wake the single drain loop so the follower runs; without it
        // the loop would sleep the full 30s backoff and this wait would time out.
        await WaitForTerminalAsync(followerJob);

        Assert.Multiple(() =>
        {
            Assert.That(parkedJob.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(followerJob.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
        });
    }

    [Test]
    public async Task LazyProvider_NullThenGenerator_RetainsQueuedJobUntilRegistration()
    {
        var generator = new RecordingGenerator();
        int providerCallCount = 0;
        IProxyGenerator? Provider()
        {
            int n = Interlocked.Increment(ref providerCallCount);
            return n == 1 ? null : generator;
        }

        await using var queue = new ProxyJobQueue(
            (Func<IProxyGenerator?>)Provider,
            store: null,
            minUnavailableBackoff: TimeSpan.FromMilliseconds(20),
            maxUnavailableBackoff: TimeSpan.FromMilliseconds(40));
        ProxyFingerprint firstSource = CreateFingerprint("lazy-first.mov");

        ProxyJob firstJob = await queue.EnqueueAsync(firstSource, ProxyPreset.Quarter);
        await WaitForTerminalAsync(firstJob);

        Assert.Multiple(() =>
        {
            Assert.That(firstJob.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(generator.Sources, Does.Contain(firstSource));
        });
    }

    [Test]
    public async Task EnqueueAsync_AlreadyCanceledToken_CompletesJobAsCanceled()
    {
        var generator = new BlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint canceledSource = CreateFingerprint("precanceled.mov");
        var canceledJobs = new List<ProxyJob>();
        queue.JobChanged += (_, e) =>
        {
            if (e.Kind == ProxyJobChangeKind.Canceled)
            {
                lock (canceledJobs)
                {
                    canceledJobs.Add(e.Job);
                }
            }
        };

        await queue.EnqueueAsync(CreateFingerprint("precancel-running.mov"), ProxyPreset.Quarter);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(
            async () => await queue.EnqueueAsync(canceledSource, ProxyPreset.Quarter, cancellationToken: cts.Token));

        ProxyJob? canceledJob;
        lock (canceledJobs)
        {
            canceledJob = canceledJobs.FirstOrDefault(j => j.Source == canceledSource);
        }

        Assert.Multiple(() =>
        {
            Assert.That(canceledJob, Is.Not.Null);
            Assert.That(canceledJob!.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending().Select(j => j.Source), Does.Not.Contain(canceledSource));
        });
        generator.Release();
    }

    [Test]
    public async Task DisposeAsync_AfterMultipleEnqueues_CancelsEveryQueuedJob()
    {
        var generator = new BlockingGenerator();
        var queue = new ProxyJobQueue(generator);

        ProxyJob running = await queue.EnqueueAsync(CreateFingerprint("dispose-running.mov"), ProxyPreset.Quarter);
        ProxyJob buffered = await queue.EnqueueAsync(CreateFingerprint("dispose-buffered.mov"), ProxyPreset.Quarter);
        ProxyJob extra = await queue.EnqueueAsync(CreateFingerprint("dispose-extra.mov"), ProxyPreset.Quarter);

        await queue.DisposeAsync();

        // Dispose's leftover sweep must cancel every queued job so nothing is left non-terminal.
        Assert.Multiple(() =>
        {
            Assert.That(running.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(buffered.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(extra.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending(), Is.Empty);
        });
    }

    // A concurrent enqueue burst must leave no job stranded at Queued: every item gets a wake permit
    // (unbounded write) and is published before it is written, so the single drain dispatches each one.
    [Test]
    public async Task EnqueueAsync_ConcurrentBurst_AllJobsReachTerminal()
    {
        var generator = new RecordingGenerator();
        await using var queue = new ProxyJobQueue(generator);

        Task<ProxyJob>[] enqueues = [.. Enumerable.Range(0, 16)
            .Select(n => queue.EnqueueAsync(CreateFingerprint($"burst-{n}.mov"), ProxyPreset.Quarter).AsTask())];

        ProxyJob[] jobs = await Task.WhenAll(enqueues);
        foreach (ProxyJob job in jobs)
        {
            await WaitForTerminalAsync(job);
        }

        Assert.That(jobs.Select(static j => j.Status), Is.All.EqualTo(ProxyJobStatus.Succeeded));
    }

    [Test]
    public async Task DisposeAsync_RacingEnqueues_LeavesNoJobNonTerminal()
    {
        for (int i = 0; i < 50; i++)
        {
            var generator = new RecordingGenerator();
            var queue = new ProxyJobQueue(generator);
            var seenJobs = new List<ProxyJob>();
            queue.JobChanged += (_, e) =>
            {
                lock (seenJobs)
                {
                    seenJobs.Add(e.Job);
                }
            };

            int iteration = i;
            Task<ProxyJob?>[] enqueues = [.. Enumerable.Range(0, 4).Select(n => Task.Run(async () =>
            {
                try
                {
                    return (ProxyJob?)await queue.EnqueueAsync(
                        CreateFingerprint($"race-{iteration}-{n}.mov"), ProxyPreset.Quarter);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or ChannelClosedException or OperationCanceledException)
                {
                    return null;
                }
            }))];

            await Task.Delay(1);
            await queue.DisposeAsync();
            ProxyJob?[] returned = await Task.WhenAll(enqueues);

            ProxyJob[] observed;
            lock (seenJobs)
            {
                observed = [.. returned.OfType<ProxyJob>().Concat(seenJobs).Distinct()];
            }

            foreach (ProxyJob job in observed)
            {
                Assert.That(
                    job.Status,
                    Is.AnyOf(ProxyJobStatus.Succeeded, ProxyJobStatus.Failed, ProxyJobStatus.Canceled, ProxyJobStatus.Skipped),
                    $"iteration {iteration}: job for {job.Source.AbsolutePath} left at {job.Status}");
            }
        }
    }

    [Test]
    public async Task JobChanged_ThrowingSubscriber_DoesNotKillQueueOrStarveOtherSubscribers()
    {
        var generator = new RecordingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        var seenKinds = new List<(ProxyJob Job, ProxyJobChangeKind Kind)>();
        queue.JobChanged += (_, _) => throw new InvalidOperationException("bad subscriber");
        queue.JobChanged += (_, e) =>
        {
            lock (seenKinds)
            {
                seenKinds.Add((e.Job, e.Kind));
            }
        };

        ProxyJob first = await queue.EnqueueAsync(CreateFingerprint("subscriber-a.mov"), ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(CreateFingerprint("subscriber-b.mov"), ProxyPreset.Quarter);

        // Wait on the recorded events, not job status: Status turns Succeeded before the
        // Succeeded notification fans out, so a status poll can win the race.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (seenKinds)
            {
                if (seenKinds.Contains((first, ProxyJobChangeKind.Succeeded))
                    && seenKinds.Contains((second, ProxyJobChangeKind.Succeeded)))
                {
                    break;
                }
            }

            await Task.Delay(10, cts.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
        });
    }

    [Test]
    public async Task GeneratorThrowsOceWithoutCancellation_ReportsFailedNotCanceled()
    {
        await using var queue = new ProxyJobQueue(new OceThrowingGenerator());
        ProxyFingerprint source = CreateFingerprint("spurious-oce.mov");

        ProxyJob job = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        await WaitForTerminalAsync(job);

        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(job.Error, Is.InstanceOf<OperationCanceledException>());
        });
    }

    [Test]
    public async Task LazyProviderThrows_FailsJobInsteadOfKillingDrainLoop()
    {
        await using var queue = new ProxyJobQueue(
            (Func<IProxyGenerator?>)(() => throw new InvalidOperationException("provider fault")),
            store: null);

        ProxyJob first = await queue.EnqueueAsync(CreateFingerprint("provider-a.mov"), ProxyPreset.Quarter);
        await WaitForTerminalAsync(first);
        ProxyJob second = await queue.EnqueueAsync(CreateFingerprint("provider-b.mov"), ProxyPreset.Quarter);
        await WaitForTerminalAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(second.Error, Is.InstanceOf<InvalidOperationException>());
        });
    }

    [Test]
    public async Task InvalidateGenerator_ReResolvesProviderAndDropsPreviousGenerator()
    {
        var first = new RecordingGenerator();
        var second = new RecordingGenerator();
        int calls = 0;
        IProxyGenerator? Provider()
        {
            int n = Interlocked.Increment(ref calls);
            return n == 1 ? first : second;
        }

        await using var queue = new ProxyJobQueue((Func<IProxyGenerator?>)Provider, store: null);
        ProxyFingerprint beforeSource = CreateFingerprint("before-invalidate.mov");
        ProxyFingerprint afterSource = CreateFingerprint("after-invalidate.mov");

        ProxyJob before = await queue.EnqueueAsync(beforeSource, ProxyPreset.Quarter);
        await WaitForTerminalAsync(before);

        // Simulates a proxy extension unloading: the cached generator must be dropped so the next
        // dispatch re-resolves from the provider instead of rooting/invoking the removed generator.
        queue.InvalidateGenerator();

        ProxyJob after = await queue.EnqueueAsync(afterSource, ProxyPreset.Quarter);
        await WaitForTerminalAsync(after);

        Assert.Multiple(() =>
        {
            Assert.That(before.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(after.Status, Is.EqualTo(ProxyJobStatus.Succeeded));
            Assert.That(first.Sources, Does.Contain(beforeSource));
            Assert.That(first.Sources, Does.Not.Contain(afterSource),
                "after invalidation the queue must not reuse the first generator");
            Assert.That(second.Sources, Does.Contain(afterSource),
                "the re-resolved generator must handle the post-invalidation job");
        });
    }

    private static async Task WaitForTerminalAsync(ProxyJob job)
    {
        // 10 s: generous against the suite's worst observed scheduling stall — the queue logic
        // under test completes in tens of milliseconds.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested
               && job.Status is not (ProxyJobStatus.Succeeded or ProxyJobStatus.Failed or ProxyJobStatus.Canceled or ProxyJobStatus.Skipped))
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private static ProxyFingerprint CreateFingerprint(string fileName)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
        return new ProxyFingerprint(path, 1, DateTime.UtcNow);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProxyEntry CreateReadyEntry(string root, ProxyFingerprint source)
    {
        string relativePath = $"{Guid.NewGuid():N}/quarter.mp4";
        string proxyPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
        File.WriteAllBytes(proxyPath, [1, 2, 3]);

        var now = DateTime.UtcNow;
        return new ProxyEntry(
            source,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            relativePath,
            3,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null);
    }

    private sealed class RecordingGenerator : IProxyGenerator
    {
        private readonly TaskCompletionSource _two = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<ProxyFingerprint> _sources = [];

        public IReadOnlyList<ProxyFingerprint> Sources
        {
            get
            {
                lock (_sources)
                {
                    return [.. _sources];
                }
            }
        }

        public ValueTask GenerateAsync(ProxyJob job)
        {
            lock (_sources)
            {
                _sources.Add(job.Source);
                if (_sources.Count == 2)
                    _two.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public Task WaitForCountAsync(int count)
        {
            return count == 2
                ? _two.Task.WaitAsync(TimeSpan.FromSeconds(5))
                : throw new ArgumentOutOfRangeException(nameof(count), "This fixture only supports waiting for exactly 2 generations.");
        }
    }

    private sealed class BlockingGenerator : IProxyGenerator
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedCount;

        public int StartedCount => Volatile.Read(ref _startedCount);

        public async ValueTask GenerateAsync(ProxyJob job)
        {
            Interlocked.Increment(ref _startedCount);
            await _release.Task.WaitAsync(job.CancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class OceThrowingGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
            => throw new OperationCanceledException("internal token, not the job's");
    }

    private sealed class SkippingGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            throw new ProxyGenerationSkippedException("ineligible");
        }
    }

    private sealed class FailingGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            throw new InvalidOperationException("encode failed");
        }
    }

    // Throws unavailable but exposes no IProxyGeneratorAvailability, so the queue can never learn it
    // recovered (mirrors a build without FFmpeg).
    private sealed class PermanentlyUnavailableGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            throw new ProxyGeneratorUnavailableException("permanently unavailable");
        }
    }

    // Simulates a generator swap racing an in-flight job: invalidates itself (as the registry-changed
    // callback would) and then throws unavailable, with no IProxyGeneratorAvailability.
    private sealed class InvalidatingUnavailableGenerator(Action invalidate) : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            invalidate();
            throw new ProxyGeneratorUnavailableException("swapped out mid-generate");
        }
    }

    // Reports availability but throws unavailable for one specific source (which therefore parks on
    // backoff) while succeeding for any other source. IsAvailable stays false so the parked job keeps
    // waiting until something wakes the drain loop; no availability event is ever raised.
    private sealed class SourceScopedUnavailableGenerator(ProxyFingerprint unavailableSource)
        : IProxyGenerator, IProxyGeneratorAvailability
    {
        private readonly TaskCompletionSource _firstUnavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => false;

        public Task FirstUnavailable => _firstUnavailable.Task;

#pragma warning disable CS0067 // Never raised: this test drives recovery via cancellation, not the event.
        public event EventHandler? AvailabilityChanged;
#pragma warning restore CS0067

        public ValueTask GenerateAsync(ProxyJob job)
        {
            if (job.Source == unavailableSource)
            {
                _firstUnavailable.TrySetResult();
                throw new ProxyGeneratorUnavailableException("scoped unavailable");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ToggleAvailabilityGenerator : IProxyGenerator, IProxyGeneratorAvailability
    {
        private readonly List<ProxyFingerprint> _succeededSources = [];
        private readonly TaskCompletionSource _unavailableHit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _available;

        public bool IsAvailable => _available;

        public Task UnavailableHit => _unavailableHit.Task;

        public IReadOnlyList<ProxyFingerprint> SucceededSources
        {
            get
            {
                lock (_succeededSources)
                {
                    return [.. _succeededSources];
                }
            }
        }

        public event EventHandler? AvailabilityChanged;

        public int AvailabilityChangedSubscriberCount => AvailabilityChanged?.GetInvocationList().Length ?? 0;

        public ValueTask GenerateAsync(ProxyJob job)
        {
            if (!IsAvailable)
            {
                _unavailableHit.TrySetResult();
                throw new ProxyGeneratorUnavailableException("missing ffmpeg");
            }

            lock (_succeededSources)
            {
                _succeededSources.Add(job.Source);
            }

            return ValueTask.CompletedTask;
        }

        public void SetAvailable()
        {
            _available = true;
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ManualRecoveryGenerator : IProxyGenerator, IProxyGeneratorAvailability
    {
        private readonly List<ProxyFingerprint> _succeededSources = [];
        private readonly TaskCompletionSource _firstUnavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _available;

        public bool IsAvailable => _available;

        public Task FirstUnavailable => _firstUnavailable.Task;

        public IReadOnlyList<ProxyFingerprint> SucceededSources
        {
            get
            {
                lock (_succeededSources)
                {
                    return [.. _succeededSources];
                }
            }
        }

#pragma warning disable CS0067 // Deliberately never raised: the queue must recover via backoff, not this event.
        public event EventHandler? AvailabilityChanged;
#pragma warning restore CS0067

        public ValueTask GenerateAsync(ProxyJob job)
        {
            if (!_available)
            {
                _firstUnavailable.TrySetResult();
                throw new ProxyGeneratorUnavailableException("transiently unavailable");
            }

            lock (_succeededSources)
            {
                _succeededSources.Add(job.Source);
            }

            return ValueTask.CompletedTask;
        }

        public void MakeAvailableSilently() => _available = true;
    }

    private sealed class ThrowingRegisterStore : IProxyStore
    {
        public string StoreRootPath => Path.Combine(TestContext.CurrentContext.WorkDirectory, "throwing-store");

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => null;

        public IReadOnlyList<ProxyEntry> Enumerate() => [];

        public void Register(ProxyEntry entry) => throw new InvalidOperationException("index locked");

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null) => false;

        public bool Delete(ProxyFingerprint source, ProxyPreset preset) => false;

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
        {
        }

        public long GetTotalBytes() => 0;

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => 0;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0067 // Not exercised by these tests.
        public event EventHandler<ProxyStoreChangedEventArgs>? Changed;
#pragma warning restore CS0067
    }

    private sealed class ControlledBlockingGenerator : IProxyGenerator
    {
        private readonly Lock _lock = new();
        private readonly Queue<TaskCompletionSource> _releases = [];
        private readonly List<ProxyFingerprint> _startedSources = [];
        private TaskCompletionSource _startedChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedCount;

        public IReadOnlyList<ProxyFingerprint> StartedSources
        {
            get
            {
                lock (_lock)
                {
                    return [.. _startedSources];
                }
            }
        }

        public async ValueTask GenerateAsync(ProxyJob job)
        {
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _releases.Enqueue(release);
                _startedSources.Add(job.Source);
                _startedCount++;
                _startedChanged.TrySetResult();
                _startedChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            await release.Task.WaitAsync(job.CancellationToken);
        }

        public async Task WaitForStartedCountAsync(int count)
        {
            while (true)
            {
                Task waitTask;
                lock (_lock)
                {
                    if (_startedCount >= count)
                        return;

                    waitTask = _startedChanged.Task;
                }

                await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public void ReleaseOne()
        {
            TaskCompletionSource? release = null;
            lock (_lock)
            {
                if (_releases.TryDequeue(out TaskCompletionSource? queued))
                    release = queued;
            }

            release?.TrySetResult();
        }

        public void ReleaseAll()
        {
            while (true)
            {
                TaskCompletionSource? release = null;
                lock (_lock)
                {
                    if (!_releases.TryDequeue(out TaskCompletionSource? queued))
                        return;

                    release = queued;
                }

                release.TrySetResult();
            }
        }
    }
}

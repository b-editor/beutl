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

    [Test]
    public async Task EnqueueAsync_DeduplicatesSourcePreset()
    {
        var generator = new BlockingGenerator();
        await using var queue = new ProxyJobQueue(generator);
        ProxyFingerprint source = CreateFingerprint("a.mov");

        ProxyJob first = await queue.EnqueueAsync(source, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(source, ProxyPreset.Quarter);

        Assert.That(second, Is.SameAs(first));
        generator.Release();
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
            capacity: 256,
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
    public async Task EnqueueAsync_CancelWhileWriteBlocked_NeverDispatchesTheJob()
    {
        // capacity 1: the worker is occupied and one permit is buffered, so this enqueue's WriteAsync
        // blocks with the item unpublished. Canceling it must remove the item cleanly without ever
        // dispatching it, and the queue must keep processing the buffered job.
        var generator = new ControlledBlockingGenerator();
        await using var queue = new ProxyJobQueue(generator, capacity: 1);
        ProxyFingerprint running = CreateFingerprint("running.mov");
        ProxyFingerprint bulk = CreateFingerprint("bulk.mov");
        ProxyFingerprint canceled = CreateFingerprint("canceled.mov");

        ProxyJob runningJob = await queue.EnqueueAsync(running, ProxyPreset.Quarter);
        await generator.WaitForStartedCountAsync(1);

        await queue.EnqueueAsync(bulk, ProxyPreset.Quarter);

        using var cts = new CancellationTokenSource();
        Task<ProxyJob> canceledEnqueue = queue.EnqueueAsync(canceled, ProxyPreset.Quarter, priority: 10, cts.Token).AsTask();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await canceledEnqueue);

        generator.ReleaseOne();
        await generator.WaitForStartedCountAsync(2);
        generator.ReleaseAll();
        await WaitForTerminalAsync(runningJob);

        Assert.That(generator.StartedSources, Does.Not.Contain(canceled),
            "a canceled enqueue whose permit was never reserved must not dispatch its job");
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
    public async Task Cancel_ParkedUnavailableJob_WakesDrainLoopWithoutWaitingBackoff()
    {
        ProxyFingerprint parked = CreateFingerprint("parked.mov");
        ProxyFingerprint follower = CreateFingerprint("follower.mov");
        var generator = new SourceScopedUnavailableGenerator(parked);
        await using var queue = new ProxyJobQueue(
            generator,
            store: null,
            capacity: 256,
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
            capacity: 256,
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
    public async Task EnqueueAsync_CanceledWhileChannelFull_CompletesJobAsCanceled()
    {
        var generator = new BlockingGenerator();
        await using var queue = new ProxyJobQueue(generator, capacity: 1);
        ProxyFingerprint blockedSource = CreateFingerprint("write-canceled-blocked.mov");
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

        // The first job parks inside the generator and the second occupies the single channel
        // slot, so the third write blocks until its token is canceled.
        await queue.EnqueueAsync(CreateFingerprint("write-canceled-running.mov"), ProxyPreset.Quarter);
        await queue.EnqueueAsync(CreateFingerprint("write-canceled-buffered.mov"), ProxyPreset.Quarter);

        using var cts = new CancellationTokenSource();
        ValueTask<ProxyJob> blocked = queue.EnqueueAsync(blockedSource, ProxyPreset.Quarter, cts.Token);
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await blocked);

        ProxyJob? blockedJob;
        lock (canceledJobs)
        {
            blockedJob = canceledJobs.FirstOrDefault(j => j.Source == blockedSource);
        }

        Assert.Multiple(() =>
        {
            Assert.That(blockedJob, Is.Not.Null);
            Assert.That(blockedJob!.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending().Select(j => j.Source), Does.Not.Contain(blockedSource));
        });
        generator.Release();
    }

    [Test]
    public async Task DisposeAsync_WhileEnqueueBlockedOnFullChannel_CompletesBlockedJobAsCanceled()
    {
        var generator = new BlockingGenerator();
        var queue = new ProxyJobQueue(generator, capacity: 1);
        ProxyFingerprint blockedSource = CreateFingerprint("dispose-blocked.mov");
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

        ProxyJob running = await queue.EnqueueAsync(CreateFingerprint("dispose-running.mov"), ProxyPreset.Quarter);
        ProxyJob buffered = await queue.EnqueueAsync(CreateFingerprint("dispose-buffered.mov"), ProxyPreset.Quarter);
        ValueTask<ProxyJob> blocked = queue.EnqueueAsync(blockedSource, ProxyPreset.Quarter);

        await queue.DisposeAsync();

        Assert.ThrowsAsync<ChannelClosedException>(async () => await blocked);

        ProxyJob? blockedJob;
        lock (canceledJobs)
        {
            blockedJob = canceledJobs.FirstOrDefault(j => j.Source == blockedSource);
        }

        Assert.Multiple(() =>
        {
            Assert.That(running.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(buffered.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(blockedJob, Is.Not.Null);
            Assert.That(blockedJob!.Status, Is.EqualTo(ProxyJobStatus.Canceled));
            Assert.That(queue.Pending(), Is.Empty);
        });
    }

    [Test]
    public async Task DisposeAsync_RacingEnqueues_LeavesNoJobNonTerminal()
    {
        for (int i = 0; i < 50; i++)
        {
            var generator = new RecordingGenerator();
            var queue = new ProxyJobQueue(generator, capacity: 1);
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

    private static async Task WaitForTerminalAsync(ProxyJob job)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
            return count == 2 ? _two.Task.WaitAsync(TimeSpan.FromSeconds(5)) : Task.CompletedTask;
        }
    }

    private sealed class BlockingGenerator : IProxyGenerator
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask GenerateAsync(ProxyJob job)
        {
            await _release.Task.WaitAsync(job.CancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }
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

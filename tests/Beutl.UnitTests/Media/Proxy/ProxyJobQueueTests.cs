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
    public async Task UnavailableGenerator_KeepsRemainingJobsQueued()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        await using var queue = new ProxyJobQueue(new UnavailableGenerator(), store);
        ProxyFingerprint firstSource = CreateFingerprint("unavailable-a.mov");
        ProxyFingerprint secondSource = CreateFingerprint("unavailable-b.mov");

        ProxyJob first = await queue.EnqueueAsync(firstSource, ProxyPreset.Quarter);
        ProxyJob second = await queue.EnqueueAsync(secondSource, ProxyPreset.Quarter);
        await WaitForTerminalAsync(first);
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ProxyJobStatus.Failed));
            Assert.That(second.Status, Is.EqualTo(ProxyJobStatus.Queued));
            Assert.That(queue.Pending(), Is.EqualTo(new[] { second }));
            Assert.That(store.TryGet(firstSource, ProxyPreset.Quarter)?.State, Is.EqualTo(ProxyState.Failed));
            Assert.That(store.TryGet(secondSource, ProxyPreset.Quarter), Is.Null);
        });
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

    private sealed class UnavailableGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            throw new ProxyGeneratorUnavailableException("missing ffmpeg");
        }
    }
}

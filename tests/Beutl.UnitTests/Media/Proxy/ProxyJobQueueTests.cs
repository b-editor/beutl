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
}

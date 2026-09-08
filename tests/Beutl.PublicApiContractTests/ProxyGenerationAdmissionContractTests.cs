using Beutl.Media.Proxy;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ProxyGenerationAdmissionContractTests : PublicApiContractTestBase
{
    [Test]
    public void Waiting_change_kind_preserves_existing_public_ordinals()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)ProxyJobChangeKind.Enqueued, Is.EqualTo(0));
            Assert.That((int)ProxyJobChangeKind.Started, Is.EqualTo(1));
            Assert.That((int)ProxyJobChangeKind.Progressed, Is.EqualTo(2));
            Assert.That((int)ProxyJobChangeKind.Succeeded, Is.EqualTo(3));
            Assert.That((int)ProxyJobChangeKind.Failed, Is.EqualTo(4));
            Assert.That((int)ProxyJobChangeKind.Canceled, Is.EqualTo(5));
            Assert.That((int)ProxyJobChangeKind.Skipped, Is.EqualTo(6));
            Assert.That((int)ProxyJobChangeKind.WaitingForAdmission, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task ExternalHost_CanImplementAndUseProxyGenerationAdmission()
    {
        AssertDoesNotHaveFriendAccess(typeof(ProxyJobQueue).Assembly);

        var generator = new TestGenerator();
        var admission = new TestAdmission();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new ProxyJobQueue(generator, store: null, admission);
        queue.JobChanged += (_, args) =>
        {
            if (args.Kind == ProxyJobChangeKind.Succeeded)
            {
                completed.TrySetResult();
            }
        };

        await queue.EnqueueAsync(
            new ProxyFingerprint(
                Path.Combine(Path.GetTempPath(), "contract-input.mov"),
                1,
                DateTime.UnixEpoch),
            ProxyPreset.Quarter);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(generator.GenerateCalls, Is.EqualTo(1));
            Assert.That(admission.AcquireCalls, Is.EqualTo(1));
            Assert.That(admission.Lease.DisposeCalls, Is.EqualTo(1));
        });
    }

    private sealed class TestAdmission : IProxyGenerationAdmission
    {
        public event EventHandler? AvailabilityChanged;

        public int AcquireCalls { get; private set; }

        public TestLease Lease { get; } = new();

        public IDisposable TryAcquireLease(ProxyJob job)
        {
            AcquireCalls++;
            return Lease;
        }

        public void SignalAvailability() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestGenerator : IProxyGenerator
    {
        public int GenerateCalls { get; private set; }

        public ValueTask GenerateAsync(ProxyJob job)
        {
            GenerateCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestLease : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }
}

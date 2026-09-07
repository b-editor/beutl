using Beutl.Media.Proxy;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ProxyGenerationAdmissionContractTests : PublicApiContractTestBase
{
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
        public int AcquireCalls { get; private set; }

        public TestLease Lease { get; } = new();

        public IDisposable TryAcquireLease(ProxyJob job)
        {
            AcquireCalls++;
            return Lease;
        }
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

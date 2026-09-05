using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LifetimeCancellationSourceTests
{
    [Test]
    public void ConcurrentCancelAndDispose_AreIdempotentAndTokenRemainsReadable()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var source = new LifetimeCancellationSource();
            CancellationToken token = source.Token;

            Assert.DoesNotThrow(() => Parallel.Invoke(
                source.Cancel,
                source.Dispose,
                source.Cancel,
                source.Dispose));
            Assert.DoesNotThrow(() => _ = token.IsCancellationRequested);
            Assert.That(token.IsCancellationRequested, Is.True);
        }
    }
}

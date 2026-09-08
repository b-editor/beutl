using Beutl.Api.Services;
using NUnit.Framework;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobRetryContractTests
{
    [Test]
    public async Task BlockedResultHasNoPreparationAndIsIdempotentlyDisposable()
    {
        AiJobRetryPreparationResult result = AiJobRetryPreparationResult.Blocked("not eligible");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsReady, Is.False);
            Assert.That(result.Explanation, Is.EqualTo("not eligible"));
        }

        Assert.Throws<InvalidOperationException>(() => result.TakePreparation());
        await result.DisposeAsync();
        await result.DisposeAsync();
    }

    [Test]
    public async Task ReadyResultTransfersPreparationOnce()
    {
        var preparation = new CountingPreparation();
        AiJobRetryPreparationResult result = AiJobRetryPreparationResult.Ready(preparation);

        IAiJobRetryPreparation transferred = result.TakePreparation();
        Assert.That(transferred, Is.SameAs(preparation));
        Assert.Throws<InvalidOperationException>(() => result.TakePreparation());

        // The result no longer owns the transferred preparation.
        await result.DisposeAsync();
        Assert.That(preparation.DisposeCount, Is.Zero);
        await transferred.DisposeAsync();
        Assert.That(preparation.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UntakenPreparationIsDisposedExactlyOnce()
    {
        var preparation = new CountingPreparation();
        AiJobRetryPreparationResult result = AiJobRetryPreparationResult.Ready(preparation);

        await result.DisposeAsync();
        await result.DisposeAsync();

        Assert.That(preparation.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void FactoriesRejectInvalidStates()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => AiJobRetryPreparationResult.Blocked(""));
            Assert.Throws<ArgumentException>(() => AiJobRetryPreparationResult.Blocked("   "));
            Assert.Throws<ArgumentNullException>(() => AiJobRetryPreparationResult.Ready(null!));
        });
    }

    private sealed class CountingPreparation : IAiJobRetryPreparation
    {
        public int DisposeCount { get; private set; }

        public Task ExecuteAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}

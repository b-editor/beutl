using Beutl.Composition;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[TestFixture]
[NonParallelizable]
public sealed class StructuralIdentityConcurrencyTests
{
    [Test]
    public async Task StructuralId_ConcurrentFirstReadersReturnTheSamePublishedValue()
    {
        using Brightness.Resource resource = new Brightness().ToResource(CompositionContext.Default);
        using var firstReady = new ManualResetEventSlim();
        using var secondReady = new ManualResetEventSlim();
        using var allowFirstPublish = new ManualResetEventSlim();
        using var allowSecondPublish = new ManualResetEventSlim();
        int arrival = 0;

        FilterEffect.Resource.StructuralIdBeforePublishForTests = current =>
        {
            if (!ReferenceEquals(current, resource))
                return;

            int order = Interlocked.Increment(ref arrival);
            if (order == 1)
            {
                firstReady.Set();
                if (!allowFirstPublish.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The first structural-id reader was not released.");
            }
            else if (order == 2)
            {
                secondReady.Set();
                if (!allowSecondPublish.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The second structural-id reader was not released.");
            }
        };

        try
        {
            Task<long> firstRead = Task.Run(() => resource.StructuralId);
            Assert.That(firstReady.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Task<long> secondRead = Task.Run(() => resource.StructuralId);
            Assert.That(secondReady.Wait(TimeSpan.FromSeconds(10)), Is.True);

            allowFirstPublish.Set();
            long first = await firstRead.WaitAsync(TimeSpan.FromSeconds(10));
            allowSecondPublish.Set();
            long second = await secondRead.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Zero);
                Assert.That(second, Is.EqualTo(first),
                    "a losing first reader must return the value already published for this resource");
                Assert.That(resource.StructuralId, Is.EqualTo(first));
            });
        }
        finally
        {
            allowFirstPublish.Set();
            allowSecondPublish.Set();
            FilterEffect.Resource.StructuralIdBeforePublishForTests = null;
        }
    }
}

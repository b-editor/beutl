using Beutl.Editor.Components.PropertyEditors.LutLibrary;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class LutThumbnailCacheTests
{
    [Test]
    public async Task Cancelling_before_a_load_starts_drops_its_entry_and_a_later_request_succeeds()
    {
        string firstPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cube");
        string secondPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cube");
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var cache = new LutThumbnailCache(1, capture: null, (path, _, _) =>
        {
            if (path == firstPath)
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }

            return Task.FromResult(new LutThumbnailResult(null, null));
        });

        try
        {
            Task<LutThumbnailResult> first = cache.GetAsync(firstPath, CancellationToken.None);
            Assert.That(firstStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "The first load did not start.");

            using var cancellation = new CancellationTokenSource();
            Task<LutThumbnailResult> cancelled = cache.GetAsync(secondPath, cancellation.Token);
            cancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () => await cancelled);
            Assert.That(cache.EntryCount, Is.EqualTo(1), "A cancelled entry must not stay cached.");

            releaseFirst.Set();
            await first;

            LutThumbnailResult later = await cache.GetAsync(secondPath, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(later.Error, Is.Null);
                Assert.That(cache.EntryCount, Is.EqualTo(2));
            });
        }
        finally
        {
            releaseFirst.Set();
        }
    }
}

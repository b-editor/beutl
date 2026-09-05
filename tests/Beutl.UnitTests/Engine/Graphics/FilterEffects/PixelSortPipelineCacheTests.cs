using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class PixelSortPipelineCacheTests
{
    [Test]
    public void GetOrCreate_KeysEverySpecializationAndReusesRepeatedValues()
    {
        int prepareCreations = 0;
        int rankCreations = 0;
        int gatherCreations = 0;
        var cache = new PixelSortPipelineCache<PipelineToken>(
            key => new PipelineToken("prepare", (int)key, prepareCreations++),
            direction => new PipelineToken("rank", (int)direction, rankCreations++),
            (direction, ascending) => new PipelineToken(
                "gather",
                ((int)direction * 2) + (ascending ? 1 : 0),
                gatherCreations++));

        PixelSortPipelines<PipelineToken> first = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Horizontal,
            ascending: true)!.Value;
        PixelSortPipelines<PipelineToken> repeated = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Horizontal,
            ascending: true)!.Value;
        PixelSortPipelines<PipelineToken> descending = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Horizontal,
            ascending: false)!.Value;
        PixelSortPipelines<PipelineToken> vertical = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Vertical,
            ascending: true)!.Value;
        PixelSortPipelines<PipelineToken> hue = cache.GetOrCreate(
            PixelSortKey.Hue,
            PixelSortDirection.Vertical,
            ascending: true)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(repeated.Prepare, Is.SameAs(first.Prepare));
            Assert.That(repeated.Rank, Is.SameAs(first.Rank));
            Assert.That(repeated.Gather, Is.SameAs(first.Gather));
            Assert.That(descending.Prepare, Is.SameAs(first.Prepare));
            Assert.That(descending.Rank, Is.SameAs(first.Rank));
            Assert.That(descending.Gather, Is.Not.SameAs(first.Gather));
            Assert.That(vertical.Prepare, Is.SameAs(first.Prepare));
            Assert.That(vertical.Rank, Is.Not.SameAs(first.Rank));
            Assert.That(vertical.Gather, Is.Not.SameAs(first.Gather));
            Assert.That(hue.Prepare, Is.Not.SameAs(first.Prepare));
            Assert.That(hue.Rank, Is.SameAs(vertical.Rank));
            Assert.That(hue.Gather, Is.SameAs(vertical.Gather));
            Assert.That(prepareCreations, Is.EqualTo(2));
            Assert.That(rankCreations, Is.EqualTo(2));
            Assert.That(gatherCreations, Is.EqualTo(3));
        });
    }

    [Test]
    public void GetOrCreate_FactoryFailureRetriesOnlyTheUnpublishedSlot()
    {
        int prepareCreations = 0;
        int rankAttempts = 0;
        int gatherCreations = 0;
        var cache = new PixelSortPipelineCache<PipelineToken>(
            key => new PipelineToken("prepare", (int)key, prepareCreations++),
            direction => ++rankAttempts == 1
                ? throw new InvalidOperationException("transient pipeline failure")
                : new PipelineToken("rank", (int)direction, rankAttempts),
            (direction, ascending) => new PipelineToken(
                "gather",
                ((int)direction * 2) + (ascending ? 1 : 0),
                gatherCreations++));

        Assert.That(
            () => cache.GetOrCreate(
                PixelSortKey.Luminance,
                PixelSortDirection.Horizontal,
                ascending: true),
            Throws.TypeOf<InvalidOperationException>());

        PixelSortPipelines<PipelineToken> recovered = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Horizontal,
            ascending: true)!.Value;
        PixelSortPipelines<PipelineToken> warmed = cache.GetOrCreate(
            PixelSortKey.Luminance,
            PixelSortDirection.Horizontal,
            ascending: true)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(warmed.Prepare, Is.SameAs(recovered.Prepare));
            Assert.That(warmed.Rank, Is.SameAs(recovered.Rank));
            Assert.That(warmed.Gather, Is.SameAs(recovered.Gather));
            Assert.That(prepareCreations, Is.EqualTo(1),
                "a successfully published prerequisite must survive a later slot failure");
            Assert.That(rankAttempts, Is.EqualTo(2),
                "the failed slot must retry and then remain warm after success");
            Assert.That(gatherCreations, Is.EqualTo(1));
        });
    }

    private sealed record PipelineToken(string Pass, int Variant, int Creation);
}

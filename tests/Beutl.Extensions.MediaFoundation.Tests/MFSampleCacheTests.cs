using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;

using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

// MFSampleCache stores Vortice IMFSample handles, which can only be created through the
// (Windows-only) Media Foundation runtime. The cache's behavior (capacity-bounded eviction,
// contiguous frame renumbering, lookup) is exercised here with real, empty samples.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFSampleCacheTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Media Foundation is only available on Windows.");
        }

        MediaFactory.MFStartup();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (OperatingSystem.IsWindows())
        {
            MediaFactory.MFShutdown();
        }
    }

    private static IMFSample CreateSample() => MediaFactory.MFCreateSample();

    [Test]
    public void LastFrameNumber_EmptyCache_ReturnsMinusOne()
    {
        var cache = new MFSampleCache(new MFSampleCacheOptions(MaxVideoBufferSize: 4));
        Assert.That(cache.LastFrameNumber(), Is.EqualTo(-1));
    }

    [Test]
    public void AddFrameSample_FirstSample_KeepsItsFrameNumber()
    {
        var cache = new MFSampleCache(new MFSampleCacheOptions(MaxVideoBufferSize: 4));
        IMFSample sample = CreateSample();

        cache.AddFrameSample(10, sample);

        Assert.Multiple(() =>
        {
            Assert.That(cache.LastFrameNumber(), Is.EqualTo(10));
            Assert.That(cache.SearchFrameSample(10), Is.SameAs(sample));
        });
    }

    [Test]
    public void AddFrameSample_SubsequentSamples_AreRenumberedContiguously()
    {
        var cache = new MFSampleCache(new MFSampleCacheOptions(MaxVideoBufferSize: 8));
        cache.AddFrameSample(10, CreateSample());   // first sample keeps frame 10
        IMFSample second = CreateSample();

        cache.AddFrameSample(50, second);           // renumbered to lastFrame + 1 == 11

        Assert.Multiple(() =>
        {
            Assert.That(cache.LastFrameNumber(), Is.EqualTo(11));
            Assert.That(cache.SearchFrameSample(11), Is.SameAs(second));
            Assert.That(cache.SearchFrameSample(50), Is.Null);
        });
    }

    [Test]
    public void AddFrameSample_BeyondCapacity_EvictsOldest()
    {
        var cache = new MFSampleCache(new MFSampleCacheOptions(MaxVideoBufferSize: 2));
        cache.AddFrameSample(0, CreateSample());    // frame 0
        IMFSample middle = CreateSample();
        cache.AddFrameSample(0, middle);            // renumbered to 1
        IMFSample newest = CreateSample();
        cache.AddFrameSample(0, newest);            // renumbered to 2, evicts frame 0

        Assert.Multiple(() =>
        {
            Assert.That(cache.SearchFrameSample(0), Is.Null, "the oldest frame should have been evicted");
            Assert.That(cache.SearchFrameSample(1), Is.SameAs(middle));
            Assert.That(cache.SearchFrameSample(2), Is.SameAs(newest));
            Assert.That(cache.LastFrameNumber(), Is.EqualTo(2));
        });
    }

    [Test]
    public void ResetVideo_ClearsCache()
    {
        var cache = new MFSampleCache(new MFSampleCacheOptions(MaxVideoBufferSize: 4));
        cache.AddFrameSample(0, CreateSample());

        cache.ResetVideo();

        Assert.Multiple(() =>
        {
            Assert.That(cache.LastFrameNumber(), Is.EqualTo(-1));
            Assert.That(cache.SearchFrameSample(0), Is.Null);
        });
    }
}

using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class EditorConfigFrameCacheTests
{
    [Test]
    public void FrameCacheMaxSize_DefaultsToHalfOfTheMemoryTheGCMayUse()
    {
        double megabytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024d);

        Assert.That(new EditorConfig().FrameCacheMaxSize, Is.EqualTo(megabytes / 2));
    }
}

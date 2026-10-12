using Beutl.Editor.Models;

namespace Beutl.UnitTests.Editor;

public class CacheBlockTests
{
    [Test]
    public void Constructor_ConvertsFrameToTimeSpan()
    {
        // 60fps, start=30 frames -> 0.5s, length=120 frames -> 2.0s
        var block = new CacheBlock(rate: 60, start: 30, length: 120, isLocked: false);
        Assert.That(block.Start, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        Assert.That(block.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }
}

using Beutl.Editor.Models;

namespace Beutl.UnitTests.Editor;

public class CacheBlockTests
{
    [Test]
    public void Constructor_ConvertsFrameToTimeSpan()
    {
        // 60fps, start=30 frames -> 0.5s, length=120 frames -> 2.0s
        var block = new CacheBlock(rate: new Rational(60), start: 30, length: 120, isLocked: false);
        Assert.That(block.Start, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        Assert.That(block.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
    }
}

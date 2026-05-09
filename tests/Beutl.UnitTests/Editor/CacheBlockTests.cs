using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

public class CacheBlockTests
{
    [Test]
    public void Constructor_StoresFrameValues()
    {
        var block = new CacheBlock(rate: 60, start: 30, length: 90, isLocked: true);
        Assert.That(block.StartFrame, Is.EqualTo(30));
        Assert.That(block.LengthFrame, Is.EqualTo(90));
        Assert.That(block.IsLocked, Is.True);
    }

    [Test]
    public void Constructor_ConvertsFrameToTimeSpan()
    {
        // 60fps, start=30 frames -> 0.5s, length=120 frames -> 2.0s
        var block = new CacheBlock(rate: 60, start: 30, length: 120, isLocked: false);
        Assert.That(block.Start, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        Assert.That(block.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void IsLocked_DefaultsToConstructorValue()
    {
        var unlocked = new CacheBlock(60, 0, 60, false);
        var locked = new CacheBlock(60, 0, 60, true);
        Assert.That(unlocked.IsLocked, Is.False);
        Assert.That(locked.IsLocked, Is.True);
    }
}

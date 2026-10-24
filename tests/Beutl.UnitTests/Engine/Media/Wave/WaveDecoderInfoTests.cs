using Beutl.Media.Wave;

namespace Beutl.UnitTests.Engine.Media.Wave;

[TestFixture]
public class WaveDecoderInfoTests
{
    [Test]
    public void Name_ListsSigned24BitPcmExactlyOnce()
    {
        string name = new WaveDecoderInfo().Name;

        Assert.That(name.Split("signed 24bit[PCM]").Length, Is.EqualTo(2));
    }
}

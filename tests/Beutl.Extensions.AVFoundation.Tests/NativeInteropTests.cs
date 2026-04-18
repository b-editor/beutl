using Beutl.Extensions.AVFoundation.Interop;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
[Platform("MacOSX")]
public class NativeInteropTests
{
    [Test]
    public void VersionReturnsPositive()
    {
        int version = BeutlAVFNative.beutl_avf_version();
        Assert.That(version, Is.GreaterThan(0));
    }

    [Test]
    public void LastErrorMessageIsEmptyInitially()
    {
        // Calling version() clears the slot; subsequent retrieval should be empty.
        _ = BeutlAVFNative.beutl_avf_version();
        Assert.That(BeutlAVFNative.GetLastErrorMessage(), Is.Empty);
    }

    [Test]
    public void FourCCToStringProducesExpectedAscii()
    {
        // 'avc1' as big-endian packed ASCII → 0x61766331
        int fourCC = ('a' << 24) | ('v' << 16) | ('c' << 8) | '1';
        Assert.That(BeutlAVFNative.FourCCToString(fourCC), Is.EqualTo("avc1"));
    }
}

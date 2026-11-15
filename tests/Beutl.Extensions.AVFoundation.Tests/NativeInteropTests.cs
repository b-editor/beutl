using System.Runtime.InteropServices;
using Beutl.Extensions.AVFoundation.Interop;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
[Platform("MacOSX")]
public class NativeInteropTests
{
    [Test]
    public void VersionMatchesManagedAbi()
    {
        int version = BeutlAVFNative.beutl_avf_version();
        Assert.That(BeutlAVFNative.ExpectedAbiVersion, Is.EqualTo(6));
        Assert.That(version, Is.EqualTo(BeutlAVFNative.ExpectedAbiVersion));
    }

    // ABI 6 dropped the channel count no one read; the output layout carries the count (#3900).
    [Test]
    public void AudioInfoMatchesTheNativeLayout()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<BeutlAudioInfo>(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<BeutlAudioInfo>(nameof(BeutlAudioInfo.CodecFourCC)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<BeutlAudioInfo>(nameof(BeutlAudioInfo.DurationNum)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<BeutlAudioInfo>(nameof(BeutlAudioInfo.DurationDen)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<BeutlAudioInfo>(nameof(BeutlAudioInfo.OutputChannelCount)).ToInt32(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<BeutlAudioInfo>(nameof(BeutlAudioInfo.SpeakerMask)).ToInt32(), Is.EqualTo(28));
        });
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

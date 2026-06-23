using Beutl.Extensions.FFmpeg.PropertyEditors;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class CodecFormatSelectionTests
{
    [Test]
    public void Resolve_CurrentPresent_SelectsItsIndexWithoutReset()
    {
        AudioFormat[] formats = [AudioFormat.Default, AudioFormat.U8, AudioFormat.S16, AudioFormat.Fltp];

        FormatSelectionResult result = CodecFormatSelection.Resolve(formats, AudioFormat.S16, AudioFormat.Default);

        Assert.That(result.ResetToSentinel, Is.False);
        Assert.That(result.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void Resolve_CurrentAbsentAndNotSentinel_ResetsToSentinelAtIndex0()
    {
        AudioFormat[] formats = [AudioFormat.Default, AudioFormat.U8, AudioFormat.S16];

        FormatSelectionResult result = CodecFormatSelection.Resolve(formats, AudioFormat.Fltp, AudioFormat.Default);

        Assert.That(result.ResetToSentinel, Is.True);
        Assert.That(result.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void Resolve_CurrentIsSentinel_KeepsSentinelWithoutReset()
    {
        AudioFormat[] formats = [AudioFormat.Default, AudioFormat.U8, AudioFormat.S16];

        FormatSelectionResult result = CodecFormatSelection.Resolve(formats, AudioFormat.Default, AudioFormat.Default);

        Assert.That(result.ResetToSentinel, Is.False);
        Assert.That(result.SelectedIndex, Is.EqualTo(0));
    }

    // The pixel-format editor keys on int with FFPixelFormat.None (-1) as the sentinel, so the same
    // generic decision must hold for value types other than the audio enum.
    [TestCase(62, false, 2)]   // present -> selected, no reset
    [TestCase(999, true, 0)]   // absent & not sentinel -> reset to sentinel
    [TestCase(-1, false, 0)]   // the sentinel itself -> no reset
    public void Resolve_IntPixelFormats_BehavesLikeEnum(int current, bool expectedReset, int expectedIndex)
    {
        int[] formats = [-1, 0, 62];

        FormatSelectionResult result = CodecFormatSelection.Resolve(formats, current, -1);

        Assert.That(result.ResetToSentinel, Is.EqualTo(expectedReset));
        Assert.That(result.SelectedIndex, Is.EqualTo(expectedIndex));
    }
}

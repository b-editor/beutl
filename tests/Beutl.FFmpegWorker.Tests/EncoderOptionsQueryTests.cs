using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegWorker.Encoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class EncoderOptionsQueryTests
{
    [OneTimeSetUp]
    public void InitializeFFmpeg()
    {
        try { FFmpegLoaderWorker.Initialize(); }
        catch (FFmpegLibrariesNotFoundException) { Assert.Ignore("FFmpeg native libraries are not available."); }
    }

    [TestCase("libx264", "preset", EncoderOptionKind.Text)]
    [TestCase("libx265", "profile", EncoderOptionKind.Text)]
    [TestCase("libvpx-vp9", "crf", EncoderOptionKind.Integer)]
    [TestCase("libaom-av1", "cpu-used", EncoderOptionKind.Integer)]
    [TestCase("libsvtav1", "preset", EncoderOptionKind.Integer)]
    [TestCase("h264_nvenc", "preset", EncoderOptionKind.Choice)]
    [TestCase("hevc_nvenc", "profile", EncoderOptionKind.Choice)]
    [TestCase("h264_qsv", "preset", EncoderOptionKind.Choice)]
    [TestCase("h264_amf", "usage", EncoderOptionKind.Choice)]
    public void DiscoversOptionsAcrossEncodersWithoutOpeningHardwareDevices(
        string codecName, string optionName, EncoderOptionKind kind)
    {
        EncoderOptionInfo option = Query(codecName).Single(o => o.Name == optionName);
        Assert.That(option.Kind, Is.EqualTo(kind));
        Assert.That(option.Description, Is.Not.Empty);
    }

    [TestCase("libx264", "medium")]
    [TestCase("libx265", "")]
    public void StringOptionsDoNotInventChoiceLists(string codecName, string nativePresetDefault)
    {
        EncoderOptionInfo[] options = Query(codecName);
        foreach (EncoderOptionInfo option in options.Where(o => o.Name is "profile" or "preset" or "tune" or "level"))
        {
            Assert.That(option.Kind, Is.EqualTo(EncoderOptionKind.Text), option.Name);
            Assert.That(option.Choices, Is.Empty, option.Name);
        }
        Assert.That(options.Single(o => o.Name == "preset").DefaultValue, Is.EqualTo(nativePresetDefault));
        Assert.That(options.Single(o => o.Name == "crf").Kind, Is.EqualTo(EncoderOptionKind.Number));
    }

    [Test]
    public void NativeConstantsPreserveNumericAliasesAndRanges()
    {
        EncoderOptionInfo preset = Query("h264_nvenc").Single(o => o.Name == "preset");
        Assert.That(preset.Choices.Any(c => c.Value == "p4" && c.NumericValue.HasValue), Is.True);
        Assert.That(preset.AllowsNumericValues, Is.True);
        Assert.That(preset.RequiresInteger, Is.True);
        Assert.That(preset.Minimum, Is.Not.Null);
        Assert.That(preset.Maximum, Is.Not.Null);
    }

    [Test]
    public void SoftwareEncoderPublishesItsOwnQualityRangeAndTuningChoices()
    {
        EncoderOptionInfo[] options = Query("libvpx-vp9");
        EncoderOptionInfo quality = options.Single(o => o.Name == "crf");
        Assert.That(quality.Minimum, Is.EqualTo(-1));
        Assert.That(quality.Maximum, Is.EqualTo(63));
        Assert.That(options.Single(o => o.Name == "deadline").Choices.Select(c => c.Value), Does.Contain("realtime"));
        Assert.That(options.Single(o => o.Name == "error-resilient").Kind, Is.EqualTo(EncoderOptionKind.Text));
        Assert.That(options.Single(o => o.Name == "error-resilient").Choices.Select(c => c.Value), Does.Contain("partitions"));
    }

    private static EncoderOptionInfo[] Query(string codecName)
    {
        if (!MediaCodec.GetCodecs().Any(c => c.IsEncoder && c.Name == codecName))
            Assert.Ignore($"{codecName} is not available.");
        return EncoderOptionsQuery.GetOptions(MediaCodec.FindEncoder(codecName), AVPixelFormat.AV_PIX_FMT_NONE);
    }
}

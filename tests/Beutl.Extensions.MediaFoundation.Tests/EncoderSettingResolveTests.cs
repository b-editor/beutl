using Beutl.Embedding.MediaFoundation;
using Beutl.Embedding.MediaFoundation.Encoding;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

// MFEncodingController instantiates a Media Foundation Sink Writer in its
// constructor, which only loads on Windows. We exercise its internal helpers
// here — they are pure functions of the settings enums and do not need the
// MF runtime, so the tests stay portable.
[Platform("Win")]
[TestFixture]
public class EncoderSettingResolveTests
{
    // -------- MapTransferToMF --------

    [Test]
    public void MapTransferToMF_HdrDefault_PicksPq()
    {
        var trc = MFEncodingController.MapTransferToMF(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Default, isHdr: true);
        Assert.That(trc, Is.EqualTo(VideoTransferFunction.Func2084));
    }

    [Test]
    public void MapTransferToMF_SdrDefault_LeavesUnknown()
    {
        // SDR encoders prefer to let downstream decoders apply their own default
        // (BT.709 for HD, BT.601 for SD), so we leave the tag unset rather than
        // pin one. A regression here would write a misleading transfer tag.
        var trc = MFEncodingController.MapTransferToMF(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Default, isHdr: false);
        Assert.That(trc, Is.EqualTo(VideoTransferFunction.FuncUnknown));
    }

    [Test]
    public void MapTransferToMF_ExplicitValueIgnoresHdrFlag()
    {
        // Once the user picks a concrete transfer, isHdr no longer matters.
        var bt709 = MFEncodingController.MapTransferToMF(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Bt709, isHdr: false);
        Assert.That(bt709, Is.EqualTo(VideoTransferFunction.Func709));

        var hlg = MFEncodingController.MapTransferToMF(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Hlg, isHdr: true);
        Assert.That(hlg, Is.EqualTo(VideoTransferFunction.FuncHlg));
    }

    // -------- MapPrimariesToMF --------

    [Test]
    public void MapPrimariesToMF_HdrDefault_PicksRec2020()
    {
        var p = MFEncodingController.MapPrimariesToMF(
            MFVideoEncoderSettings.ColorPrimariesType.Default, isHdr: true);
        Assert.That(p, Is.EqualTo(VideoPrimaries.Bt2020));
    }

    [Test]
    public void MapPrimariesToMF_SdrDefault_LeavesUnknown()
    {
        var p = MFEncodingController.MapPrimariesToMF(
            MFVideoEncoderSettings.ColorPrimariesType.Default, isHdr: false);
        Assert.That(p, Is.EqualTo(VideoPrimaries.Unknown));
    }

    // -------- MapMatrixToMF --------

    [Test]
    public void MapMatrixToMF_HdrDefault_PicksBt2020Ncl()
    {
        var m = MFEncodingController.MapMatrixToMF(
            MFVideoEncoderSettings.YCbCrMatrixType.Default, isHdr: true);
        Assert.That(m, Is.EqualTo(VideoTransferMatrix.Bt202010));
    }

    [Test]
    public void MapMatrixToMF_SdrDefault_LeavesUnknown()
    {
        var m = MFEncodingController.MapMatrixToMF(
            MFVideoEncoderSettings.YCbCrMatrixType.Default, isHdr: false);
        Assert.That(m, Is.EqualTo(VideoTransferMatrix.Unknown));
    }

    // Pins the explicit-value branches so a switch typo (e.g. Rec2020 → Bt709)
    // cannot slip through silently. The same parameterization covers HDR and
    // SDR — explicit values must not depend on the isHdr flag.
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Bt709, VideoTransferMatrix.Bt709)]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Bt601, VideoTransferMatrix.Bt601)]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Rec2020, VideoTransferMatrix.Bt202010)]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Smpte240M, VideoTransferMatrix.Smpte240m)]
    public void MapMatrixToMF_ExplicitValues(
        MFVideoEncoderSettings.YCbCrMatrixType input, VideoTransferMatrix expected)
    {
        Assert.That(MFEncodingController.MapMatrixToMF(input, isHdr: false), Is.EqualTo(expected));
        Assert.That(MFEncodingController.MapMatrixToMF(input, isHdr: true), Is.EqualTo(expected));
    }

    // -------- ResolveSdrYuvMatrix --------

    [Test]
    public void ResolveSdrYuvMatrix_DefaultMapsToBt709()
    {
        var m = MFEncodingController.ResolveSdrYuvMatrix(
            MFVideoEncoderSettings.YCbCrMatrixType.Default);
        // Compare every coefficient — a wrong preset would match on Yg but
        // disagree on the chroma rows.
        AssertMatrixEquals(m, PixelFormatConverter.YuvMatrix8.Bt709);
    }

    [Test]
    public void ResolveSdrYuvMatrix_DefaultAndTagDisagreeIntentionally()
    {
        // Documents the intentional asymmetry: pixels are written with BT.709
        // coefficients but the matrix tag is Unknown so downstream decoders
        // can pick their own default (BT.709 / BT.601 depending on resolution).
        var pixels = MFEncodingController.ResolveSdrYuvMatrix(
            MFVideoEncoderSettings.YCbCrMatrixType.Default);
        var tag = MFEncodingController.MapMatrixToMF(
            MFVideoEncoderSettings.YCbCrMatrixType.Default, isHdr: false);
        AssertMatrixEquals(pixels, PixelFormatConverter.YuvMatrix8.Bt709);
        Assert.That(tag, Is.EqualTo(VideoTransferMatrix.Unknown));
    }

    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Bt601, "Bt601")]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Bt709, "Bt709")]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Rec2020, "Bt2020")]
    [TestCase(MFVideoEncoderSettings.YCbCrMatrixType.Smpte240M, "Smpte240M")]
    public void ResolveSdrYuvMatrix_ExplicitValues(
        MFVideoEncoderSettings.YCbCrMatrixType input, string presetName)
    {
        var resolved = MFEncodingController.ResolveSdrYuvMatrix(input);
        var expected = presetName switch
        {
            "Bt601" => PixelFormatConverter.YuvMatrix8.Bt601,
            "Bt709" => PixelFormatConverter.YuvMatrix8.Bt709,
            "Bt2020" => PixelFormatConverter.YuvMatrix8.Bt2020,
            "Smpte240M" => PixelFormatConverter.YuvMatrix8.Smpte240M,
            _ => throw new ArgumentOutOfRangeException(nameof(presetName)),
        };
        AssertMatrixEquals(resolved, expected);
    }

    private static void AssertMatrixEquals(
        PixelFormatConverter.YuvMatrix8 actual,
        PixelFormatConverter.YuvMatrix8 expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Yr, Is.EqualTo(expected.Yr), "Yr");
            Assert.That(actual.Yg, Is.EqualTo(expected.Yg), "Yg");
            Assert.That(actual.Yb, Is.EqualTo(expected.Yb), "Yb");
            Assert.That(actual.Cbr, Is.EqualTo(expected.Cbr), "Cbr");
            Assert.That(actual.Cbg, Is.EqualTo(expected.Cbg), "Cbg");
            Assert.That(actual.Cbb, Is.EqualTo(expected.Cbb), "Cbb");
            Assert.That(actual.Crr, Is.EqualTo(expected.Crr), "Crr");
            Assert.That(actual.Crg, Is.EqualTo(expected.Crg), "Crg");
            Assert.That(actual.Crb, Is.EqualTo(expected.Crb), "Crb");
        });
    }

    // -------- MapTransferForHelper / MapPrimariesForHelper --------

    [Test]
    public void MapTransferForHelper_SdrDefaultIsSrgb()
    {
        // Previously this returned Func2084 (PQ) regardless of HDR mode, which
        // applied PQ tone mapping to sRGB pixels in the SDR path.
        var trc = MFEncodingController.MapTransferForHelper(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Default, isHdr: false);
        Assert.That(trc, Is.EqualTo(VideoTransferFunction.FuncSRGB));
    }

    [Test]
    public void MapTransferForHelper_HdrDefaultIsPq()
    {
        var trc = MFEncodingController.MapTransferForHelper(
            MFVideoEncoderSettings.ColorTransferCharacteristic.Default, isHdr: true);
        Assert.That(trc, Is.EqualTo(VideoTransferFunction.Func2084));
    }

    [Test]
    public void MapPrimariesForHelper_SdrDefaultIsBt709()
    {
        var p = MFEncodingController.MapPrimariesForHelper(
            MFVideoEncoderSettings.ColorPrimariesType.Default, isHdr: false);
        Assert.That(p, Is.EqualTo(VideoPrimaries.Bt709));
    }

    [Test]
    public void MapPrimariesForHelper_HdrDefaultIsRec2020()
    {
        var p = MFEncodingController.MapPrimariesForHelper(
            MFVideoEncoderSettings.ColorPrimariesType.Default, isHdr: true);
        Assert.That(p, Is.EqualTo(VideoPrimaries.Bt2020));
    }

    // -------- IsAudioOnlyContainer --------

    [TestCase(".m4a", true)]
    [TestCase(".wav", true)]
    [TestCase(".mp3", true)]
    [TestCase(".aac", true)]
    [TestCase(".adts", true)]
    [TestCase(".mp4", false)]
    [TestCase(".mov", false)]
    [TestCase(".asf", false)]
    [TestCase("", false)]
    [TestCase(".M4A", true)]   // case-insensitive audio
    [TestCase(".MP4", false)]  // case-insensitive video
    public void IsAudioOnlyContainer(string extension, bool expected)
    {
        bool result = MFEncodingController.IsAudioOnlyContainer("/tmp/sample" + extension);
        Assert.That(result, Is.EqualTo(expected));
    }

    // -------- ClampAudioChannels --------

    [TestCase(0, 2, true)]               // unset → stereo
    [TestCase(-1, 2, true)]              // negative → stereo
    [TestCase(int.MinValue, 2, true)]    // pathological negative
    [TestCase(1, 1, false)]              // mono passthrough
    [TestCase(2, 2, false)]              // stereo passthrough
    [TestCase(3, 2, true)]               // ≥3 channels (5.0/quad)
    [TestCase(4, 2, true)]
    [TestCase(5, 2, true)]
    [TestCase(6, 2, true)]               // 5.1
    [TestCase(7, 2, true)]
    [TestCase(8, 2, true)]               // 7.1
    [TestCase(int.MaxValue, 2, true)]    // pathological positive
    public void ClampAudioChannels(int requested, int expected, bool expectedClamped)
    {
        int resolved = MFEncodingController.ClampAudioChannels(requested, out bool clamped);
        Assert.That(resolved, Is.EqualTo(expected));
        Assert.That(clamped, Is.EqualTo(expectedClamped));
    }
}

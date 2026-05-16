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

    // -------- ResolveSdrYuvMatrix --------

    [Test]
    public void ResolveSdrYuvMatrix_DefaultMapsToBt709()
    {
        var m = MFEncodingController.ResolveSdrYuvMatrix(
            MFVideoEncoderSettings.YCbCrMatrixType.Default);
        // YuvMatrix8 is a struct without an explicit name; identify via a known
        // coefficient (Bt709.Yg == 183).
        Assert.That(m.Yg, Is.EqualTo(PixelFormatConverter.YuvMatrix8.Bt709.Yg));
    }

    [Test]
    public void ResolveSdrYuvMatrix_Bt601ExplicitlySelected()
    {
        var m = MFEncodingController.ResolveSdrYuvMatrix(
            MFVideoEncoderSettings.YCbCrMatrixType.Bt601);
        Assert.That(m.Yg, Is.EqualTo(PixelFormatConverter.YuvMatrix8.Bt601.Yg));
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
    public void IsAudioOnlyContainer(string extension, bool expected)
    {
        bool result = MFEncodingController.IsAudioOnlyContainer("/tmp/sample" + extension);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsAudioOnlyContainer_CaseInsensitive()
    {
        Assert.That(MFEncodingController.IsAudioOnlyContainer("/tmp/x.M4A"), Is.True);
        Assert.That(MFEncodingController.IsAudioOnlyContainer("/tmp/x.MP4"), Is.False);
    }

    // -------- ClampAudioChannels --------

    [TestCase(0, 2, true)]    // unset → stereo
    [TestCase(-1, 2, true)]   // negative → stereo
    [TestCase(1, 1, false)]   // mono passthrough
    [TestCase(2, 2, false)]   // stereo passthrough
    [TestCase(6, 2, true)]    // 5.1 clamped to stereo (no source data)
    [TestCase(8, 2, true)]    // 7.1 clamped to stereo
    public void ClampAudioChannels(int requested, int expected, bool expectedClamped)
    {
        int resolved = MFEncodingController.ClampAudioChannels(requested, out bool clamped);
        Assert.That(resolved, Is.EqualTo(expected));
        Assert.That(clamped, Is.EqualTo(expectedClamped));
    }
}

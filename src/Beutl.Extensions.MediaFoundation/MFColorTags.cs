using Beutl.Media;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

// Translates between Media Foundation's color attributes and Beutl's color tags.
internal static class MFColorTags
{
    // The YUY2 output is 8-bit SDR, so HDR streams keep the whole untagged BT.601 limited-range sRGB reading.
    internal static VideoColorTags Read(IMFMediaType type)
    {
        var transfer = (VideoTransferFunction)MediaFactory.MFGetAttributeUInt32(
            type, MediaTypeAttributeKeys.TransferFunction, 0);
        if (transfer is VideoTransferFunction.Func2084 or VideoTransferFunction.FuncHlg)
            return default;

        return new VideoColorTags(
            Matrix: ToMatrix((VideoTransferMatrix)MediaFactory.MFGetAttributeUInt32(
                type, MediaTypeAttributeKeys.YuvMatrix, 0)),
            Range: ToRange((NominalRange)MediaFactory.MFGetAttributeUInt32(
                type, MediaTypeAttributeKeys.VideoNominalRange, 0)),
            Transfer: ToTransfer(transfer),
            Primaries: ToPrimaries((VideoPrimaries)MediaFactory.MFGetAttributeUInt32(
                type, MediaTypeAttributeKeys.VideoPrimaries, 0)));
    }

    // Untagged settings leave the type untagged. Input YUV pixels already match these output tags, and BgraToI420
    // writes the limited range that the YCbCr defaults declare.
    internal static void Apply(IMFMediaType type, VideoColorTags color)
    {
        if (color.Matrix == VideoColorMatrix.Unknown)
            return;

        type.Set(MediaTypeAttributeKeys.VideoPrimaries, (uint)(color.Primaries == BitmapColorPrimaries.Rec2020
            ? VideoPrimaries.Bt2020 : VideoPrimaries.Bt709)).CheckError();
        type.Set(MediaTypeAttributeKeys.TransferFunction, (uint)(color.Transfer == BitmapColorTransfer.Srgb
            ? VideoTransferFunction.FuncSRGB : VideoTransferFunction.Func709)).CheckError();
        type.Set(MediaTypeAttributeKeys.VideoNominalRange, (uint)NominalRange.Range16_235).CheckError();
        type.Set(MediaTypeAttributeKeys.YuvMatrix, (uint)(color.Matrix == VideoColorMatrix.Bt601
            ? VideoTransferMatrix.Bt601 : VideoTransferMatrix.Bt709)).CheckError();
    }

    private static VideoColorMatrix ToMatrix(VideoTransferMatrix matrix) => matrix switch
    {
        VideoTransferMatrix.Bt709 => VideoColorMatrix.Bt709,
        VideoTransferMatrix.Bt601 => VideoColorMatrix.Bt601,
        VideoTransferMatrix.Bt202010 or VideoTransferMatrix.Bt202012 => VideoColorMatrix.Bt2020,
        _ => VideoColorMatrix.Unknown,
    };

    private static VideoColorRange ToRange(NominalRange range) => range switch
    {
        NominalRange.Range0_255 => VideoColorRange.Full,
        NominalRange.Range16_235 => VideoColorRange.Limited,
        _ => VideoColorRange.Unknown,
    };

    private static BitmapColorTransfer ToTransfer(VideoTransferFunction transfer) => transfer switch
    {
        VideoTransferFunction.Func10 => BitmapColorTransfer.Linear,
        VideoTransferFunction.Func22 => BitmapColorTransfer.TwoDotTwo,
        VideoTransferFunction.Func28 => BitmapColorTransfer.Gamma28,
        VideoTransferFunction.Func709 => BitmapColorTransfer.Bt709,
        VideoTransferFunction.Func2020 or VideoTransferFunction.Func2020Const => BitmapColorTransfer.Rec2020,
        VideoTransferFunction.Func240m => BitmapColorTransfer.Smpte240M,
        VideoTransferFunction.FuncSRGB => BitmapColorTransfer.Srgb,
        _ => BitmapColorTransfer.Unknown,
    };

    private static BitmapColorPrimaries ToPrimaries(VideoPrimaries primaries) => primaries switch
    {
        VideoPrimaries.Bt709 => BitmapColorPrimaries.Bt709,
        VideoPrimaries.Bt4702SysM => BitmapColorPrimaries.Bt470M,
        VideoPrimaries.Bt4702SysBG => BitmapColorPrimaries.Bt470BG,
        VideoPrimaries.Smpte170m or VideoPrimaries.SmpteC => BitmapColorPrimaries.Smpte170M,
        VideoPrimaries.Smpte240m => BitmapColorPrimaries.Smpte240M,
        VideoPrimaries.Ebu3213 => BitmapColorPrimaries.Ebu3213,
        VideoPrimaries.Bt2020 => BitmapColorPrimaries.Rec2020,
        VideoPrimaries.Xyz => BitmapColorPrimaries.Xyz,
        VideoPrimaries.DciP3 => BitmapColorPrimaries.Smpte431,
        VideoPrimaries.DisplayP3 => BitmapColorPrimaries.Dcip3,
        _ => BitmapColorPrimaries.Unknown,
    };
}

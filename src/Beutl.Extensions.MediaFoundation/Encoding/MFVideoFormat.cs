using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

// MFVideoFormatとVideoFormatGuidsを相互に変換するクラス
public static class MFVideoFormatExtension
{
    // MFVideoFormatからVideoFormatGuidsに変換する
    public static Guid ToVideoFormatGuid(this MFVideoFormat format)
    {
        return format switch
        {
            MFVideoFormat.Wmv1 => VideoFormatGuids.Wmv1,
            MFVideoFormat.Wmv2 => VideoFormatGuids.Wmv2,
            MFVideoFormat.Wmv3 => VideoFormatGuids.Wmv3,
            MFVideoFormat.Dvc => VideoFormatGuids.Dvc,
            MFVideoFormat.Dv50 => VideoFormatGuids.Dv50,
            MFVideoFormat.Dv25 => VideoFormatGuids.Dv25,
            MFVideoFormat.H263 => VideoFormatGuids.H263,
            MFVideoFormat.H264 => VideoFormatGuids.H264,
            MFVideoFormat.H265 => VideoFormatGuids.H265,
            MFVideoFormat.Hevc => VideoFormatGuids.Hevc,
            MFVideoFormat.HevcEs => VideoFormatGuids.HevcEs,
            MFVideoFormat.Vp80 => VideoFormatGuids.Vp80,
            MFVideoFormat.Vp90 => VideoFormatGuids.Vp90,
            MFVideoFormat.MultisampledS2 => VideoFormatGuids.MultisampledS2,
            MFVideoFormat.M4S2 => VideoFormatGuids.M4S2,
            MFVideoFormat.Wvc1 => VideoFormatGuids.Wvc1,
            MFVideoFormat.P010 => VideoFormatGuids.P010,
            MFVideoFormat.AI44 => VideoFormatGuids.AI44,
            MFVideoFormat.Dvh1 => VideoFormatGuids.Dvh1,
            MFVideoFormat.Dvhd => VideoFormatGuids.Dvhd,
            MFVideoFormat.MultisampledS1 => VideoFormatGuids.MultisampledS1,
            MFVideoFormat.Mp43 => VideoFormatGuids.Mp43,
            MFVideoFormat.Mp4s => VideoFormatGuids.Mp4s,
            MFVideoFormat.Mp4v => VideoFormatGuids.Mp4v,
            MFVideoFormat.Mpg1 => VideoFormatGuids.Mpg1,
            MFVideoFormat.Mjpg => VideoFormatGuids.Mjpg,
            MFVideoFormat.Dvsl => VideoFormatGuids.Dvsl,
            MFVideoFormat.YUY2 => VideoFormatGuids.YUY2,
            MFVideoFormat.Yv12 => VideoFormatGuids.Yv12,
            MFVideoFormat.P016 => VideoFormatGuids.P016,
            MFVideoFormat.P210 => VideoFormatGuids.P210,
            MFVideoFormat.P216 => VideoFormatGuids.P216,
            MFVideoFormat.I420 => VideoFormatGuids.I420,
            MFVideoFormat.Dvsd => VideoFormatGuids.Dvsd,
            MFVideoFormat.Y42T => VideoFormatGuids.Y42T,
            MFVideoFormat.NV12 => VideoFormatGuids.NV12,
            MFVideoFormat.NV11 => VideoFormatGuids.NV11,
            MFVideoFormat.Y210 => VideoFormatGuids.Y210,
            MFVideoFormat.Y216 => VideoFormatGuids.Y216,
            MFVideoFormat.Y410 => VideoFormatGuids.Y410,
            MFVideoFormat.Y416 => VideoFormatGuids.Y416,
            MFVideoFormat.Y41P => VideoFormatGuids.Y41P,
            MFVideoFormat.Y41T => VideoFormatGuids.Y41T,
            MFVideoFormat.Yvu9 => VideoFormatGuids.Yvu9,
            MFVideoFormat.Yvyu => VideoFormatGuids.Yvyu,
            MFVideoFormat.Iyuv => VideoFormatGuids.Iyuv,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }
}

public enum MFVideoFormat
{
    // SharpDX.MediaFoundation.VideoFormatGuidsから作成
    Wmv1,
    Wmv2,
    Wmv3,
    Dvc,
    Dv50,
    Dv25,
    H263,
    H264,
    H265,
    Hevc,
    HevcEs,
    Vp80,
    Vp90,
    MultisampledS2,
    M4S2,
    Wvc1,
    P010,
    AI44,
    Dvh1,
    Dvhd,
    MultisampledS1,
    Mp43,
    Mp4s,
    Mp4v,
    Mpg1,
    Mjpg,
    Dvsl,
    YUY2,
    Yv12,
    P016,
    P210,
    P216,
    I420,
    Dvsd,
    Y42T,
    NV12,
    NV11,
    Y210,
    Y216,
    Y410,
    Y416,
    Y41P,
    Y41T,
    Yvu9,
    Yvyu,
    Iyuv
}

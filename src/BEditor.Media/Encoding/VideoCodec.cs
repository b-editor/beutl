using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// This enum contains only supported video encoders.
    /// If you want to use a codec not included to this enum, you can cast <see cref="AVCodecID"/> to <see cref="VideoCodec"/>.
    /// </summary>
    public enum VideoCodec
    {
        /// <summary>
        /// Default video codec for the selected container format.
        /// </summary>
        Default = AVCodecID.AV_CODEC_ID_NONE,

        /// <summary>
        /// MPEG-1 video codec.
        /// </summary>
        MPEG = AVCodecID.AV_CODEC_ID_MPEG1VIDEO,

        /// <summary>
        /// MPEG-2 (H.262) video codec.
        /// </summary>
        MPEG2 = AVCodecID.AV_CODEC_ID_MPEG2VIDEO,

        /// <summary>
        /// H.263 codec
        /// </summary>
        H263 = AVCodecID.AV_CODEC_ID_H263,

        /// <summary>
        /// Motion JPEG video codec.
        /// </summary>
        MJPEG = AVCodecID.AV_CODEC_ID_MJPEG,

        /// <summary>
        /// MPEG-4 Part 2 video codec.
        /// </summary>
        MPEG4 = AVCodecID.AV_CODEC_ID_MPEG4,

        /// <summary>
        /// H.263-P codec
        /// </summary>
        H263P = AVCodecID.AV_CODEC_ID_H263P,

        /// <summary>
        /// H.263-I codec
        /// </summary>
        H263I = AVCodecID.AV_CODEC_ID_H263I,

        /// <summary>
        /// DV codec.
        /// </summary>
        DV = AVCodecID.AV_CODEC_ID_DVVIDEO,

        /// <summary>
        /// Advanced Video Coding (AVC) - H.264 codec
        /// </summary>
        H264 = AVCodecID.AV_CODEC_ID_H264,

        /// <summary>
        /// Theora codec.
        /// </summary>
        Theora = AVCodecID.AV_CODEC_ID_THEORA,

        /// <summary>
        /// Cinepak codec.
        /// </summary>
        Cinepak = AVCodecID.AV_CODEC_ID_CINEPAK,

        /// <summary>
        /// Microsoft Windows Media Video 9 (WMV3)
        /// </summary>
        WMV = AVCodecID.AV_CODEC_ID_WMV3,

        /// <summary>
        /// Dirac codec.
        /// </summary>
        Dirac = AVCodecID.AV_CODEC_ID_DIRAC,

        /// <summary>
        /// VP8 codec.
        /// </summary>
        VP8 = AVCodecID.AV_CODEC_ID_VP8,

        /// <summary>
        /// VP9 codec.
        /// </summary>
        VP9 = AVCodecID.AV_CODEC_ID_VP9,

        /// <summary>
        /// High Efficiency Video Coding (HEVC) - H.265 codec
        /// </summary>
        H265 = AVCodecID.AV_CODEC_ID_HEVC,

        /// <summary>
        /// AV1 codec.
        /// </summary>
        AV1 = AVCodecID.AV_CODEC_ID_AV1,
    }
}
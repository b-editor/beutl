using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// This enum contains only supported audio encoders.
    /// If you want to use a codec not included to this enum, you can cast <see cref="AVCodecID"/> to <see cref="AudioCodec"/>.
    /// </summary>
    public enum AudioCodec
    {
        /// <summary>
        /// Default audio codec for the selected container format.
        /// </summary>
        Default = AVCodecID.AV_CODEC_ID_NONE,

        /// <summary>
        /// MP3 (MPEG audio layer 3) audio codec
        /// </summary>
        MP3 = AVCodecID.AV_CODEC_ID_MP3,

        /// <summary>
        /// AAC (Advanced Audio Coding) audio codec
        /// </summary>
        AAC = AVCodecID.AV_CODEC_ID_AAC,

        /// <summary>
        /// ATSC A/52A (AC-3) audio codec
        /// </summary>
        AC3 = AVCodecID.AV_CODEC_ID_AC3,

        /// <summary>
        /// OGG Vorbis audio codec
        /// </summary>
        Vorbis = AVCodecID.AV_CODEC_ID_VORBIS,

        /// <summary>
        /// Windows Media Audio V2 audio codec
        /// </summary>
        WMA = AVCodecID.AV_CODEC_ID_WMAV2,
    }
}
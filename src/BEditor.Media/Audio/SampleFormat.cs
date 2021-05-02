using FFmpeg.AutoGen;

namespace BEditor.Media.Audio
{
    /// <summary>
    /// Enumerates common audio sample formats supported by FFmpeg.
    /// </summary>
    public enum SampleFormat
    {
        /// <summary>
        /// Unsupported/Unknown.
        /// </summary>
        None = AVSampleFormat.AV_SAMPLE_FMT_NONE,

        /// <summary>
        /// Unsigned 8-bit integer.
        /// </summary>
        UnsignedByte = AVSampleFormat.AV_SAMPLE_FMT_U8,

        /// <summary>
        /// Signed 16-bit integer.
        /// </summary>
        SignedWord = AVSampleFormat.AV_SAMPLE_FMT_S16,

        /// <summary>
        /// Signed 32-bit integer.
        /// </summary>
        SignedDWord = AVSampleFormat.AV_SAMPLE_FMT_S32,

        /// <summary>
        /// Single precision floating point.
        /// </summary>
        Single = AVSampleFormat.AV_SAMPLE_FMT_FLT,

        /// <summary>
        /// Double precision floating point.
        /// </summary>
        Double = AVSampleFormat.AV_SAMPLE_FMT_DBL,

        /// <summary>
        /// Signed 16-bit integer (planar).
        /// </summary>
        SignedWordP = AVSampleFormat.AV_SAMPLE_FMT_S16P,

        /// <summary>
        /// Signed 32-bit integer (planar).
        /// </summary>
        SignedDWordP = AVSampleFormat.AV_SAMPLE_FMT_S32P,

        /// <summary>
        /// Single precision floating point (planar).
        /// </summary>
        SingleP = AVSampleFormat.AV_SAMPLE_FMT_FLTP,

        /// <summary>
        /// Double precision floating point (planar).
        /// </summary>
        DoubleP = AVSampleFormat.AV_SAMPLE_FMT_DBLP,
    }
}

using System.Collections.Generic;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a set of demuxer options and flags.
    /// See https://ffmpeg.org/ffmpeg-formats.html#Format-Options.
    /// </summary>
    public class ContainerOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerOptions"/> class with the default settings.
        /// </summary>
        public ContainerOptions()
        {
        }

        /// <summary>
        /// Discard corrupted packets.
        /// Port of 'discardcorrupt'.
        /// </summary>
        public bool FlagDiscardCorrupt { get; set; }

        /// <summary>
        /// Enable fast, but inaccurate seeks for some formats.
        /// Port of 'fastseek'.
        /// </summary>
        public bool FlagEnableFastSeek { get; set; }

        /// <summary>
        /// Do not fill in missing values that can be exactly calculated.
        /// Port of 'nofillin'.
        /// </summary>
        public bool FlagEnableNoFillIn { get; set; }

        /// <summary>
        /// Generate missing PTS if DTS is present.
        /// Port of 'genpts'.
        /// </summary>
        public bool FlagGeneratePts { get; set; }

        /// <summary>
        /// Ignore DTS if PTS is set.
        /// Port of 'igndts'.
        /// </summary>
        public bool FlagIgnoreDts { get; set; }

        /// <summary>
        /// Ignore index.
        /// Port of 'ignidx'.
        /// </summary>
        public bool FlagIgnoreIndex { get; set; }

        /// <summary>
        /// Reduce the latency introduced by optional buffering.
        /// Port of 'nobuffer'.
        /// </summary>
        public bool FlagNoBuffer { get; set; }

        /// <summary>
        /// Try to interleave output packets by DTS.
        /// Port of 'sortdts'.
        /// </summary>
        public bool FlagSortDts { get; set; }

        /// <summary>
        /// Allow seeking to non-keyframes on demuxer level when supported.
        /// Port of seek2any.
        /// </summary>
        public bool SeekToAny { get; set; }

        /// <summary>
        /// Gets or sets the private demuxer-specific options.
        /// </summary>
        public Dictionary<string, string> PrivateOptions { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Applies flag settings specified in this class to an instance of <see cref="AVFormatContext"/>.
        /// </summary>
        /// <param name="formatContext">An empty instance of <see cref="AVFormatContext"/> before opening the stream.</param>
        internal unsafe void ApplyFlags(AVFormatContext* formatContext)
        {
            ref var formatFlags = ref formatContext->flags;

            if (FlagDiscardCorrupt)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT;
            }

            if (FlagEnableFastSeek)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_FAST_SEEK;
            }

            if (FlagEnableNoFillIn)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_NOFILLIN;
            }

            if (FlagGeneratePts)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_GENPTS;
            }

            if (FlagIgnoreDts)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_IGNDTS;
            }

            if (FlagIgnoreIndex)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_IGNIDX;
            }

            if (FlagNoBuffer)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_NOBUFFER;
            }

            if (FlagSortDts)
            {
                formatFlags |= ffmpeg.AVFMT_FLAG_SORT_DTS;
            }

            formatContext->seek2any = SeekToAny ? 1 : 0;
        }
    }
}

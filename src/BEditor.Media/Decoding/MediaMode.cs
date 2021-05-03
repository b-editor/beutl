using System;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents the audio/video streams loading modes.
    /// </summary>
    [Flags]
    public enum MediaMode
    {
        /// <summary>
        /// Enables loading only video streams.
        /// </summary>
        Video = 1 << AVMediaType.AVMEDIA_TYPE_VIDEO,

        /// <summary>
        /// Enables loading only audio streams.
        /// </summary>
        Audio = 1 << AVMediaType.AVMEDIA_TYPE_AUDIO,

        /// <summary>
        /// Enables loading both audio and video streams if they exist.
        /// </summary>
        AudioVideo = Audio | Video,
    }
}
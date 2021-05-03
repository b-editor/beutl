using System.ComponentModel;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Video container formats supported by BEditor.Media.
    /// </summary>
    public enum ContainerFormat
    {
        /// <summary>
        /// The 3GPP container format (.3gp)
        /// </summary>
        [Description("3gp")]
        Container3GP,

        /// <summary>
        /// The 3GPP2 container format (.3g2)
        /// </summary>
        [Description("3g2")]
        Container3GP2,

        /// <summary>
        /// The Microsoft Advanced Systems Formats container format (.asf)
        /// Use this container when encoding a .wmv (Windows Media) video file.
        /// </summary>
        [Description("asf")]
        ASF,

        /// <summary>
        /// The Audio Video Interleave container format (.avi)
        /// </summary>
        [Description("avi")]
        AVI,

        /// <summary>
        /// The Flash Video container format (.flv)
        /// </summary>
        [Description("flv")]
        FLV,

        /// <summary>
        /// The Matroska Multimedia Container format (.mkv)
        /// </summary>
        [Description("mkv")]
        MKV,

        /// <summary>
        /// The QuickTime container format (.mov)
        /// </summary>
        [Description("mov")]
        MOV,

        /// <summary>
        /// The MPEG-4 container format (.mp4)
        /// </summary>
        [Description("mp4")]
        MP4,

        /// <summary>
        /// The Ogg container format (.ogv extension for video files)
        /// </summary>
        [Description("ogv")]
        Ogg,

        /// <summary>
        /// The WebM container format (.webm)
        /// </summary>
        [Description("webm")]
        WebM,
    }
}
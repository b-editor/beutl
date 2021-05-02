using FFmpeg.AutoGen;

namespace BEditor.Media.Graphics
{
    /// <summary>
    /// Represents the most used image pixel formats. Partially compatible with <see cref="AVPixelFormat"/>.
    /// </summary>
    public enum ImagePixelFormat
    {
        /// <summary>
        /// Represents a YUV 12bpp 4:2:0 video pixel format.
        /// </summary>
        Yuv420 = AVPixelFormat.AV_PIX_FMT_YUV420P,

        /// <summary>
        /// Represents a RGB 24bpp bitmap pixel format.
        /// </summary>
        Rgb24 = AVPixelFormat.AV_PIX_FMT_RGB24,

        /// <summary>
        /// Represents a BGR 24bpp bitmap pixel format. Used by default in GDI+ and WPF graphics.
        /// </summary>
        Bgr24 = AVPixelFormat.AV_PIX_FMT_BGR24,

        /// <summary>
        /// Represents a YUV 16bpp 4:2:2 video pixel format.
        /// </summary>
        Yuv422 = AVPixelFormat.AV_PIX_FMT_YUV422P,

        /// <summary>
        /// Represents a YUV 24bpp 4:4:4 video pixel format.
        /// </summary>
        Yuv444 = AVPixelFormat.AV_PIX_FMT_YUV444P,

        /// <summary>
        /// Represents a UYVY422 pixel format.
        /// </summary>
        Uyvy422 = AVPixelFormat.AV_PIX_FMT_UYVY422,

        /// <summary>
        /// Represents a ARGB(with alpha channel) 32bpp bitmap pixel format.
        /// </summary>
        Argb32 = AVPixelFormat.AV_PIX_FMT_ARGB,

        /// <summary>
        /// Represents a RGBA(with alpha channel) 32bpp bitmap pixel format.
        /// </summary>
        Rgba32 = AVPixelFormat.AV_PIX_FMT_RGBA,

        /// <summary>
        /// Represents a BGRA(with alpha channel) 32bpp bitmap pixel format.
        /// </summary>
        Bgra32 = AVPixelFormat.AV_PIX_FMT_BGRA,
    }
}
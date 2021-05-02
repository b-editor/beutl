using System.Collections.Generic;

using BEditor.Media.Graphics;

using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a video encoder configuration.
    /// </summary>
    public class VideoEncoderSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoEncoderSettings"/> class with default video settings values.
        /// </summary>
        /// <param name="width">The video frame width.</param>
        /// <param name="height">The video frame height.</param>
        /// <param name="framerate">The video frames per seconds (fps) value.</param>
        /// <param name="codec">The video encoder.</param>
        public VideoEncoderSettings(int width, int height, int framerate = 30, VideoCodec codec = VideoCodec.Default, ImagePixelFormat format = ImagePixelFormat.Yuv420)
        {
            VideoWidth = width;
            VideoHeight = height;
            Framerate = framerate;
            Codec = codec;
            VideoFormat = format;
            CodecOptions = new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets or sets the video stream bitrate (bytes per second). The default value is 5,000,000 B/s.
        /// If CRF (for H.264/H.265) is set, this value will be ignored.
        /// </summary>
        public int Bitrate { get; set; } = 5_000_000;

        /// <summary>
        /// Gets or sets the GoP value. The default value is 12.
        /// </summary>
        public int KeyframeRate { get; set; } = 12;

        /// <summary>
        /// Gets or sets the video frame width.
        /// </summary>
        public int VideoWidth { get; set; }

        /// <summary>
        /// Gets or sets the video frame height.
        /// </summary>
        public int VideoHeight { get; set; }

        /// <summary>
        /// Gets or sets the output video pixel format. The default value is YUV420p.
        /// Added frames will be automatically converted to this format.
        /// </summary>
        public ImagePixelFormat VideoFormat { get; set; }

        /// <summary>
        /// Gets or sets video frame rate (FPS) value. The default value is 30 frames/s.
        /// </summary>
        public int Framerate
        {
            get => FramerateRational.num / FramerateRational.den;
            set => FramerateRational = new AVRational { num = value, den = 1 };
        }

        /// <summary>
        /// Gets or sets the video frame rate as a FFmpeg <see cref="AVRational"/> value. Optional. Overwrites <see cref="Framerate"/> property.
        /// </summary>
        public AVRational FramerateRational { get; set; }

        /// <summary>
        /// Gets the calculated time base for the video stream. Value is always equal to reciporical of <see cref="FramerateRational"/>.
        /// </summary>
        public AVRational TimeBase => new() { num = FramerateRational.den, den = FramerateRational.num };

        /// <summary>
        /// Gets or sets the Constant Rate Factor. It supports only H.264 and H.265 codecs.
        /// </summary>
        public int? CRF { get; set; }

        /// <summary>
        /// Gets or sets the encoder preset. It supports only H.264 and H.265 codecs.
        /// </summary>
        public EncoderPreset EncoderPreset { get; set; }

        /// <summary>
        /// Gets or sets the dictionary with custom codec options.
        /// </summary>
        public Dictionary<string, string> CodecOptions { get; set; }

        /// <summary>
        /// Gets or sets the codec for this stream.
        /// If set to <see cref="VideoCodec.Default"/>, encoder will use default video codec for current container.
        /// </summary>
        public VideoCodec Codec { get; set; }
    }
}

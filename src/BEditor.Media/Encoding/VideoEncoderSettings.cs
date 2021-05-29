// VideoEncoderSettings.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

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
        public VideoEncoderSettings(int width, int height, int framerate = 30)
        {
            VideoWidth = width;
            VideoHeight = height;
            Framerate = framerate;
            CodecOptions = new();
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
        /// Gets or sets video frame rate (FPS) value. The default value is 30 frames/s.
        /// </summary>
        public int Framerate { get; set; }

        /// <summary>
        /// Gets or sets the dictionary with custom codec options.
        /// </summary>
        public Dictionary<string, object> CodecOptions { get; set; }
    }
}
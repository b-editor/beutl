// VideoStreamInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents informations about the video stream.
    /// </summary>
    public sealed class VideoStreamInfo : StreamInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoStreamInfo"/> class.
        /// </summary>
        /// <param name="codecName">The codec name of the stream.</param>
        /// <param name="type">The media type of the stream.</param>
        /// <param name="duration">The duration of the stream.</param>
        /// <param name="framesize">The video frame dimensions.</param>
        /// <param name="framenum">The number of frames.</param>
        [Obsolete("Use VideoStreamInfo(string, MediaType, TimeSpan, Size, int, int)", true)]
        public VideoStreamInfo(string codecName, MediaType type, TimeSpan duration, Size framesize, int framenum)
            : base(codecName, type, duration)
        {
            NumberOfFrames = framenum;
            FrameSize = framesize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoStreamInfo"/> class.
        /// </summary>
        /// <param name="codecName">The codec name of the stream.</param>
        /// <param name="type">The media type of the stream.</param>
        /// <param name="duration">The duration of the stream.</param>
        /// <param name="framesize">The video frame dimensions.</param>
        /// <param name="framenum">The number of frames.</param>
        /// <param name="framerate">The frame rate.</param>
        public VideoStreamInfo(string codecName, MediaType type, TimeSpan duration, Size framesize, int framenum, int framerate)
            : base(codecName, type, duration)
        {
            FrameRate = framerate;
            NumberOfFrames = framenum;
            FrameSize = framesize;
        }

        /// <summary>
        /// Gets the frame rate.
        /// </summary>
        public int FrameRate { get; }

        /// <summary>
        /// Gets the number of frames value taken from the container metadata or estimated in constant frame rate videos. Returns null if not available.
        /// </summary>
        public int NumberOfFrames { get; }

        /// <summary>
        /// Gets the video frame dimensions.
        /// </summary>
        public Size FrameSize { get; }
    }
}
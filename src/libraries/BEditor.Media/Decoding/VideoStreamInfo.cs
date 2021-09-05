// VideoStreamInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Linq;

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
        /// <param name="framerate">The frame rate.</param>
        [Obsolete("To be addded.")]
        public VideoStreamInfo(string codecName, MediaType type, TimeSpan duration, Size framesize, int framenum, int framerate)
            : base(codecName, type, duration)
        {
            RealFrameRate = new(framerate);
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
        public VideoStreamInfo(string codecName, MediaType type, TimeSpan duration, Size framesize, int framenum, Rational framerate)
            : base(codecName, type, duration)
        {
            RealFrameRate = framerate;
            NumberOfFrames = framenum;
            FrameSize = framesize;
        }

        /// <summary>
        /// Gets the frame rate.
        /// </summary>
        [Obsolete("Use AvgFrameRate.")]
        public int FrameRate => (int)AvgFrameRate;

        /// <summary>
        /// Gets the average frame rate.
        /// </summary>
        public float AvgFrameRate => RealFrameRate;

        /// <summary>
        /// Gets the frame rate.
        /// </summary>
        public Rational RealFrameRate { get; }

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
// IVideoOutputStream.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a video encoder stream.
    /// </summary>
    public interface IVideoOutputStream : IDisposable
    {
        /// <summary>
        /// Gets the video encoding configuration used to create this stream.
        /// </summary>
        public VideoEncoderSettings Configuration { get; }

        /// <summary>
        /// Gets the current duration of this stream.
        /// </summary>
        public TimeSpan CurrentDuration { get; }

        /// <summary>
        /// Writes the specified bitmap to the video stream as the next frame.
        /// </summary>
        /// <param name="image">The image to write.</param>
        public void AddFrame(Image<BGRA32> image);
    }
}
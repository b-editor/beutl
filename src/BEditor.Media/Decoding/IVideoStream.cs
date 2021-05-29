// IVideoStream.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a video stream in the <see cref="MediaFile"/>.
    /// </summary>
    public interface IVideoStream : IMediaStream
    {
        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public new VideoStreamInfo Info { get; }

        /// <inheritdoc/>
        StreamInfo IMediaStream.Info => Info;

        /// <summary>
        /// Reads the next frame from the video stream.
        /// </summary>
        /// <returns>A decoded bitmap.</returns>
        public Image<BGRA32> GetNextFrame();

        /// <summary>
        /// Reads the next frame from the video stream.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="image">The decoded video frame.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetNextFrame([NotNullWhen(true)] out Image<BGRA32>? image);

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <returns>The decoded video frame.</returns>
        public Image<BGRA32> GetFrame(TimeSpan time);

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="image">The decoded video frame.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetFrame(TimeSpan time, [NotNullWhen(true)] out Image<BGRA32>? image);
    }
}
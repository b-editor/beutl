// StreamInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents generic informations about the stream, specialized by subclasses for specific
    /// kinds of streams.
    /// </summary>
    public abstract class StreamInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamInfo"/> class.
        /// </summary>
        /// <param name="codecName">The codec name of the stream.</param>
        /// <param name="type">The media type of the stream.</param>
        /// <param name="duration">The duration of the stream.</param>
        protected StreamInfo(string codecName, MediaType type, TimeSpan duration)
        {
            (CodecName, Type, Duration) = (codecName, type, duration);
        }

        /// <summary>
        /// Gets the codec name.
        /// </summary>
        public string CodecName { get; }

        /// <summary>
        /// Gets the stream's type.
        /// </summary>
        public MediaType Type { get; }

        /// <summary>
        /// Gets the stream duration.
        /// </summary>
        public TimeSpan Duration { get; }
    }
}
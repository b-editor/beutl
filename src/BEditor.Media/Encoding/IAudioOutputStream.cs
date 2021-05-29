// IAudioOutputStream.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Media.PCM;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents an audio encoder stream.
    /// </summary>
    public interface IAudioOutputStream : IDisposable
    {
        /// <summary>
        /// Gets the video encoding configuration used to create this stream.
        /// </summary>
        public AudioEncoderSettings Configuration { get; }

        /// <summary>
        /// Gets the current duration of this stream.
        /// </summary>
        public TimeSpan CurrentDuration { get; }

        /// <summary>
        /// Writes the specified audio data to the stream as the next frame.
        /// </summary>
        /// <param name="sound">The audio data to write.</param>
        public void AddFrame(Sound<StereoPCMFloat> sound);
    }
}
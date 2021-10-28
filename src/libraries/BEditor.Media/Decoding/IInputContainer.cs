// IInputContainer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents the multimedia file container.
    /// </summary>
    public interface IInputContainer : IDisposable
    {
        /// <summary>
        /// Gets the video streams.
        /// </summary>
        public IVideoStream[] Video { get; }

        /// <summary>
        /// Gets the audio streams.
        /// </summary>
        public IAudioStream[] Audio { get; }

        /// <summary>
        /// Gets informations about the media container.
        /// </summary>
        public MediaInfo Info { get; }
    }
}
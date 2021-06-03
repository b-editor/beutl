// IOutputContainer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents the multimedia file container used for encoding.
    /// </summary>
    public interface IOutputContainer : IDisposable
    {
        /// <summary>
        /// Gets the video streams.
        /// </summary>
        public IEnumerable<IVideoOutputStream> Video { get; }

        /// <summary>
        /// Gets the audio streams.
        /// </summary>
        public IEnumerable<IAudioOutputStream> Audio { get; }

        /// <summary>
        /// Applies a set of metadata fields to the output file.
        /// </summary>
        /// <param name="metadata">The metadata object to set.</param>
        public void SetMetadata(ContainerMetadata metadata);

        /// <summary>
        /// Adds a new video stream to the container. Usable only in encoding, before locking file.
        /// </summary>
        /// <param name="config">The stream configuration.</param>
        public void AddVideoStream(VideoEncoderSettings config);

        /// <summary>
        /// Adds a new audio stream to the container. Usable only in encoding, before locking file.
        /// </summary>
        /// <param name="config">The stream configuration.</param>
        public void AddAudioStream(AudioEncoderSettings config);

        /// <summary>
        /// Gets the default settings for video encoder.
        /// </summary>
        /// <returns>Returns the default encoder setting.</returns>
        [Obsolete("Use IRegisterdEncoding.GetDefaultVideoSettings")]
        public VideoEncoderSettings GetDefaultVideoSettings();

        /// <summary>
        /// Gets the default settings for audio encoder.
        /// </summary>
        /// <returns>Returns the default encoder setting.</returns>
        [Obsolete("Use IRegisterdEncoding.GetDefaultAudioSettings")]
        public AudioEncoderSettings GetDefaultAudioSettings();

        /// <summary>
        /// Creates a multimedia file.
        /// </summary>
        /// <returns>A new <see cref="MediaOutput"/>.</returns>
        public MediaOutput Create();
    }
}
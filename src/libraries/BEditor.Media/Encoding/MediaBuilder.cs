// MediaBuilder.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a multimedia file creator.
    /// </summary>
    public sealed class MediaBuilder
    {
        private readonly IOutputContainer _container;
        private readonly IRegisterdEncoding _encoding;

        private MediaBuilder(IOutputContainer container, IRegisterdEncoding encoding)
        {
            _container = container;
            _encoding = encoding;
        }

        /// <summary>
        /// Sets up a multimedia container with the format guessed from the file extension.
        /// </summary>
        /// <param name="path">A path to create the output file.</param>
        /// <exception cref="NotSupportedException">Not supported format.</exception>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public static MediaBuilder CreateContainer(string path)
        {
            EncodingRegistory.Create(path, out var container, out var encoding);
            if (container is null) throw new NotSupportedException("Not supported format.");

            return new(container, encoding!);
        }

        /// <summary>
        /// Sets the multimedia container from an instance of <see cref="IRegisterdEncoding"/>.
        /// </summary>
        /// <param name="path">A path to create the output file.</param>
        /// <param name="encoding">The encoding.</param>
        /// <exception cref="NotSupportedException">Not supported format.</exception>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public static MediaBuilder CreateContainer(string path, IRegisterdEncoding encoding)
        {
            var container = encoding.Create(path) ?? throw new NotSupportedException("Not supported format.");

            return new(container, encoding);
        }

        /// <summary>
        /// Applies a set of metadata fields to the output file.
        /// </summary>
        /// <param name="metadata">The metadata object to set.</param>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public MediaBuilder UseMetadata(ContainerMetadata metadata)
        {
            _container.SetMetadata(metadata);
            return this;
        }

        /// <summary>
        /// Adds a new video stream to the file.
        /// </summary>
        /// <param name="settings">The video stream settings.</param>
        /// <returns>This <see cref="MediaBuilder"/> object.</returns>
        public MediaBuilder WithVideo(Action<VideoEncoderSettings> settings)
        {
            var config = _encoding is ISupportEncodingSettings sp ? sp.GetDefaultVideoSettings() : new VideoEncoderSettings(1920, 1080);
            settings.Invoke(config);

            _container.AddVideoStream(config);
            return this;
        }

        /// <summary>
        /// Adds a new audio stream to the file.
        /// </summary>
        /// <param name="settings">The video stream settings.</param>
        /// <returns>This <see cref="MediaBuilder"/> object.</returns>
        public MediaBuilder WithAudio(Action<AudioEncoderSettings> settings)
        {
            var config = _encoding is ISupportEncodingSettings sp ? sp.GetDefaultAudioSettings() : new AudioEncoderSettings(44100, 2);
            settings.Invoke(config);

            _container.AddAudioStream(config);
            return this;
        }

        /// <summary>
        /// Adds a new video stream to the file.
        /// </summary>
        /// <returns>This <see cref="MediaBuilder"/> object.</returns>
        public MediaBuilder WithVideo()
        {
            var config = _encoding is ISupportEncodingSettings sp ? sp.GetDefaultVideoSettings() : new VideoEncoderSettings(1920, 1080);

            _container.AddVideoStream(config);
            return this;
        }

        /// <summary>
        /// Adds a new audio stream to the file.
        /// </summary>
        /// <returns>This <see cref="MediaBuilder"/> object.</returns>
        public MediaBuilder WithAudio()
        {
            var config = _encoding is ISupportEncodingSettings sp ? sp.GetDefaultAudioSettings() : new AudioEncoderSettings(44100, 2);

            _container.AddAudioStream(config);
            return this;
        }

        /// <summary>
        /// Creates a multimedia file.
        /// </summary>
        /// <returns>A new <see cref="MediaOutput"/>.</returns>
        public MediaOutput Create()
        {
            return _container.Create();
        }
    }
}
using System;
using System.IO;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a multimedia file creator.
    /// </summary>
    public sealed class MediaBuilder
    {
        private readonly IOutputContainer _container;

        private MediaBuilder(IOutputContainer container)
        {
            _container = container;
        }

        /// <summary>
        /// Sets up a multimedia container with the format guessed from the file extension.
        /// </summary>
        /// <param name="path">A path to create the output file.</param>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public static MediaBuilder CreateContainer(string path)
        {
            var container = EncoderFactory.Create(path) ?? throw new NotSupportedException("Not supported format.");

            return new(container);
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
            var config = _container.GetDefaultVideoSettings();
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
            var config = _container.GetDefaultAudioSettings();
            settings.Invoke(config);

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
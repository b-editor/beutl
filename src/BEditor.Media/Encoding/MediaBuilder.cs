using System;
using System.IO;

using BEditor.Media.Common;
using BEditor.Media.Encoding.Internal;
using BEditor.Media.Helpers;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a multimedia file creator.
    /// </summary>
    public sealed class MediaBuilder
    {
        private readonly OutputContainer _container;
        private readonly string _outputPath;

        private MediaBuilder(string path, ContainerFormat? format)
        {
            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException($"The path \"{path}\" is not valid.");
            }

            if (!Path.HasExtension(path) && format == null)
            {
                throw new ArgumentException("The file path has no extension.");
            }

            _container = OutputContainer.Create(format?.GetDescription() ?? Path.GetExtension(path));
            _outputPath = path;
        }

        /// <summary>
        /// Sets up a multimedia container with the specified <paramref name="format"/>.
        /// </summary>
        /// <param name="path">A path to create the output file.</param>
        /// <param name="format">A container format.</param>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public static MediaBuilder CreateContainer(string path, ContainerFormat format)
        {
            return new(path, format);
        }

        /// <summary>
        /// Sets up a multimedia container with the format guessed from the file extension.
        /// </summary>
        /// <param name="path">A path to create the output file.</param>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public static MediaBuilder CreateContainer(string path)
        {
            return new(path, null);
        }

        /// <summary>
        /// Applies a custom container option.
        /// </summary>
        /// <param name="key">The option key.</param>
        /// <param name="value">The value to set.</param>
        /// <returns>The <see cref="MediaBuilder"/> instance.</returns>
        public MediaBuilder UseFormatOption(string key, string value)
        {
            _container.ContainerOptions[key] = value;
            return this;
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
        public MediaBuilder WithVideo(VideoEncoderSettings settings)
        {
            if (FFmpegLoader.IsFFmpegGplLicensed == false && (settings.Codec == VideoCodec.H264 || settings.Codec == VideoCodec.H265))
            {
                throw new NotSupportedException("The LGPL-licensed FFmpeg build does not contain libx264 and libx265 codecs.");
            }

            _container.AddVideoStream(settings);
            return this;
        }

        /// <summary>
        /// Adds a new audio stream to the file.
        /// </summary>
        /// <param name="settings">The video stream settings.</param>
        /// <returns>This <see cref="MediaBuilder"/> object.</returns>
        public MediaBuilder WithAudio(AudioEncoderSettings settings)
        {
            _container.AddAudioStream(settings);
            return this;
        }

        /// <summary>
        /// Creates a multimedia file for specified video stream.
        /// </summary>
        /// <returns>A new <see cref="MediaOutput"/>.</returns>
        public MediaOutput Create()
        {
            _container.CreateFile(_outputPath);

            return new MediaOutput(_container);
        }
    }
}
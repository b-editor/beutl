// MediaFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.Linq;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a multimedia file.
    /// </summary>
    public sealed class MediaFile : IDisposable
    {
        private readonly IInputContainer _container;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaFile"/> class.
        /// </summary>
        /// <param name="container">The input container.</param>
        public MediaFile(IInputContainer container)
        {
            _container = container;

            VideoStreams = container.Video.ToArray();
            AudioStreams = container.Audio.ToArray();

            Info = container.Info;
        }

        /// <summary>
        /// Gets all the video streams in the media file.
        /// </summary>
        public IVideoStream[] VideoStreams { get; }

        /// <summary>
        /// Gets the first video stream in the media file.
        /// </summary>
        public IVideoStream? Video => VideoStreams.FirstOrDefault();

        /// <summary>
        /// Gets a value indicating whether the file contains video streams.
        /// </summary>
        public bool HasVideo => VideoStreams.Length > 0;

        /// <summary>
        /// Gets all the audio streams in the media file.
        /// </summary>
        public IAudioStream[] AudioStreams { get; }

        /// <summary>
        /// Gets the first audio stream in the media file.
        /// </summary>
        public IAudioStream? Audio => AudioStreams.FirstOrDefault();

        /// <summary>
        /// Gets a value indicating whether the file contains video streams.
        /// </summary>
        public bool HasAudio => AudioStreams.Length > 0;

        /// <summary>
        /// Gets informations about the media container.
        /// </summary>
        public MediaInfo Info { get; }

        /// <summary>
        /// Opens a media file from the specified path with default settings.
        /// </summary>
        /// <param name="path">A path to the media file.</param>
        /// <returns>The opened <see cref="MediaFile"/>.</returns>
        public static MediaFile Open(string path)
        {
            return Open(path, new MediaOptions());
        }

        /// <summary>
        /// Opens a media file from the specified path.
        /// </summary>
        /// <param name="path">A path to the media file.</param>
        /// <param name="options">The decoder settings.</param>
        /// <returns>The opened <see cref="MediaFile"/>.</returns>
        public static MediaFile Open(string path, MediaOptions options)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (!File.Exists(path)) throw new FileNotFoundException(null, path);

            var container = DecodingRegistory.Open(path, options);
            return container is not null
                ? new MediaFile(container)
                : throw new DecoderNotFoundException();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _container.Dispose();
            GC.SuppressFinalize(this);
            _isDisposed = true;
        }
    }
}
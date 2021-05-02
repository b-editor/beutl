using System;
using System.IO;
using System.Linq;

using BEditor.Media.Common;
using BEditor.Media.Decoding.Internal;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a multimedia file.
    /// </summary>
    public sealed class MediaFile : IDisposable
    {
        private readonly InputContainer _container;
        private bool _isDisposed;

        private unsafe MediaFile(InputContainer container, MediaOptions options)
        {
            _container = container;

            var video = container.Decoders.Where(codec => codec?.Info.Type == MediaType.Video);
            var audio = container.Decoders.Where(codec => codec?.Info.Type == MediaType.Audio);

            VideoStreams = video.Select(codec => new VideoStream(codec!, options)).ToArray();
            AudioStreams = audio.Select(codec => new AudioStream(codec!, options)).ToArray();

            Info = new MediaInfo(container.Pointer);
        }

        /// <summary>
        /// Gets all the video streams in the media file.
        /// </summary>
        public VideoStream[] VideoStreams { get; }

        /// <summary>
        /// Gets the first video stream in the media file.
        /// </summary>
        public VideoStream? Video => VideoStreams.FirstOrDefault();

        /// <summary>
        /// Gets a value indicating whether the file contains video streams.
        /// </summary>
        public bool HasVideo => VideoStreams.Length > 0;

        /// <summary>
        /// Gets all the audio streams in the media file.
        /// </summary>
        public AudioStream[] AudioStreams { get; }

        /// <summary>
        /// Gets the first audio stream in the media file.
        /// </summary>
        public AudioStream? Audio => AudioStreams.FirstOrDefault();

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
            try
            {
                var container = InputContainer.LoadFile(path, options);
                return new MediaFile(container, options);
            }
            catch (DirectoryNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to open the media file", ex);
            }
        }

        /// <summary>
        /// Opens a media stream with default settings.
        /// </summary>
        /// <param name="stream">A stream of the multimedia file.</param>
        /// <returns>The opened <see cref="MediaFile"/>.</returns>
        public static MediaFile Open(Stream stream)
        {
            return Open(stream, new MediaOptions());
        }

        /// <summary>
        /// Opens a media stream.
        /// </summary>
        /// <param name="stream">A stream of the multimedia file.</param>
        /// <param name="options">The decoder settings.</param>
        /// <returns>The opened <see cref="MediaFile"/>.</returns>
        public static MediaFile Open(Stream stream, MediaOptions options)
        {
            try
            {
                var container = InputContainer.LoadStream(stream, options);
                return new MediaFile(container, options);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to open the media stream", ex);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            var video = VideoStreams.Cast<MediaStream>();
            var audio = AudioStreams.Cast<MediaStream>();

            foreach (var stream in video.Concat(audio))
            {
                stream.Dispose();
            }

            _container.Dispose();
            GC.SuppressFinalize(this);
            _isDisposed = true;
        }
    }
}
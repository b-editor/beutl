using System;
using System.Linq;

using BEditor.Media.Encoding.Internal;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a multimedia output file.
    /// </summary>
    public class MediaOutput : IDisposable
    {
        private readonly OutputContainer _container;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaOutput"/> class.
        /// </summary>
        /// <param name="mediaContainer">The <see cref="OutputContainer"/> object.</param>
        internal MediaOutput(OutputContainer mediaContainer)
        {
            _container = mediaContainer;

            VideoStreams = _container.Video
                .Select(o => new VideoOutputStream(o.stream, o.config))
                .ToArray();

            AudioStreams = _container.Audio
                .Select(o => new AudioOutputStream(o.stream, o.config))
                .ToArray();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="MediaOutput"/> class.
        /// </summary>
        ~MediaOutput()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the video streams in the media file.
        /// </summary>
        public VideoOutputStream[] VideoStreams { get; }

        /// <summary>
        /// Gets the audio streams in the media file.
        /// </summary>
        public AudioOutputStream[] AudioStreams { get; }

        /// <summary>
        /// Gets the first video stream in the media file.
        /// </summary>
        public VideoOutputStream? Video => VideoStreams.FirstOrDefault();

        /// <summary>
        /// Gets the first audio stream in the media file.
        /// </summary>
        public AudioOutputStream? Audio => AudioStreams.FirstOrDefault();

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

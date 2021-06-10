// MediaOutput.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a multimedia output file.
    /// </summary>
    public sealed class MediaOutput : IDisposable
    {
        private readonly IOutputContainer _container;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaOutput"/> class.
        /// </summary>
        /// <param name="container">The <see cref="IOutputContainer"/> object.</param>
        public MediaOutput(IOutputContainer container)
        {
            _container = container;
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
        public IEnumerable<IVideoOutputStream> VideoStreams => _container.Video;

        /// <summary>
        /// Gets the audio streams in the media file.
        /// </summary>
        public IEnumerable<IAudioOutputStream> AudioStreams => _container.Audio;

        /// <summary>
        /// Gets the first video stream in the media file.
        /// </summary>
        public IVideoOutputStream? Video => VideoStreams.FirstOrDefault();

        /// <summary>
        /// Gets the first audio stream in the media file.
        /// </summary>
        public IAudioOutputStream? Audio => AudioStreams.FirstOrDefault();

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
// MediaInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Contains informations about the media container.
    /// </summary>
    public class MediaInfo
    {
        private readonly Lazy<FileInfo?> _fileInfo;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaInfo"/> class.
        /// </summary>
        /// <param name="filepath">The file path used to open the container.</param>
        /// <param name="format">The container format name.</param>
        /// <param name="bitrate">The container bitrate in bytes per second (B/s) units. 0 if unknown.</param>
        /// <param name="duration">The duration of the media container.</param>
        /// <param name="starttime">The start time of the media container.</param>
        /// <param name="metadata">The container file metadata. Streams may contain additional metadata.</param>
        public MediaInfo(string filepath, string format, long bitrate, TimeSpan duration, TimeSpan starttime, ContainerMetadata metadata)
        {
            (FilePath, ContainerFormat, Bitrate, Duration, StartTime, Metadata) = (filepath, format, bitrate, duration, starttime, metadata);

            _fileInfo = new Lazy<FileInfo?>(() =>
            {
                try
                {
                    return new FileInfo(FilePath);
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Gets the file path used to open the container.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets a <see cref="System.IO.FileInfo"/> object for the media file.
        /// It contains file size, directory, last access, creation and write timestamps.
        /// Returns <see langword="null"/> if not available, for example when <see cref="Stream"/> was used to open the <see cref="MediaFile"/>.
        /// </summary>
        public FileInfo? FileInfo => _fileInfo.Value;

        /// <summary>
        /// Gets the container format name.
        /// </summary>
        public string ContainerFormat { get; }

        /// <summary>
        /// Gets the container bitrate in bytes per second (B/s) units. 0 if unknown.
        /// </summary>
        public long Bitrate { get; }

        /// <summary>
        /// Gets the duration of the media container.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the start time of the media container.
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets the container file metadata. Streams may contain additional metadata.
        /// </summary>
        public ContainerMetadata Metadata { get; }
    }
}
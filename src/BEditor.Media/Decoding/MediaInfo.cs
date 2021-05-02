using System;
using System.Collections.ObjectModel;
using System.IO;

using BEditor.Media.Common;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

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
        /// <param name="container">The input container context.</param>
        internal unsafe MediaInfo(AVFormatContext* container)
        {
            FilePath = new IntPtr(container->url).Utf8ToString();
            ContainerFormat = new IntPtr(container->iformat->name).Utf8ToString();
            Metadata = new ContainerMetadata(container->metadata);
            Bitrate = container->bit_rate > 0 ? container->bit_rate : 0;

            var timeBase = new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE };
            Duration = container->duration != ffmpeg.AV_NOPTS_VALUE ?
                    container->duration.ToTimeSpan(timeBase) :
                    TimeSpan.Zero;
            StartTime = container->start_time != ffmpeg.AV_NOPTS_VALUE ?
                    container->start_time.ToTimeSpan(timeBase) :
                    TimeSpan.Zero;
            Chapters = new ReadOnlyCollection<MediaChapter>(ParseChapters(container));

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

        /// <summary>
        /// Gets a collection of chapters existing in the media file.
        /// </summary>
        public ReadOnlyCollection<MediaChapter> Chapters { get; }

        private static unsafe MediaChapter[] ParseChapters(AVFormatContext* container)
        {
            var streamChapters = new MediaChapter[container->nb_chapters];

            for (var i = 0; i < container->nb_chapters; i++)
            {
                var chapter = container->chapters[i];
                var meta = chapter->metadata;
                var startTimespan = chapter->start.ToTimeSpan(chapter->time_base);
                var endTimespan = chapter->end.ToTimeSpan(chapter->time_base);
                streamChapters[i] =
                        new MediaChapter(startTimespan, endTimespan, FFDictionary.ToDictionary(meta, true));
            }

            return streamChapters;
        }
    }
}
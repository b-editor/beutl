using System;
using System.Collections.ObjectModel;

using BEditor.Media.Common;
using BEditor.Media.Decoding.Internal;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents generic informations about the stream, specialized by subclasses for specific
    /// kinds of streams.
    /// </summary>
    public class StreamInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamInfo"/> class.
        /// </summary>
        /// <param name="stream">A generic stream.</param>
        /// <param name="type">The media type of the stream.</param>
        /// <param name="container">The input container.</param>
        internal unsafe StreamInfo(AVStream* stream, MediaType type, InputContainer container)
        {
            var codec = stream->codec;
            Metadata = new ReadOnlyDictionary<string, string>(FFDictionary.ToDictionary(stream->metadata));
            CodecName = ffmpeg.avcodec_get_name(codec->codec_id);
            CodecId = codec->codec_id.FormatEnum(12);
            Index = stream->index;
            Type = type;

            TimeBase = stream->time_base;
            RealFrameRate = stream->r_frame_rate;
            AvgFrameRate = stream->avg_frame_rate.ToDouble();
            IsVariableFrameRate = RealFrameRate.ToDouble() != AvgFrameRate;

            if (stream->duration >= 0)
            {
                Duration = stream->duration.ToTimeSpan(stream->time_base);
                DurationRaw = stream->duration;
            }
            else
            {
                Duration = TimeSpan.FromTicks(container.Pointer->duration * 10);
                DurationRaw = Duration.ToTimestamp(TimeBase);
            }

            if (stream->start_time >= 0)
            {
                StartTime = stream->start_time.ToTimeSpan(stream->time_base);
            }

            if (stream->nb_frames > 0)
            {
                IsFrameCountProvidedByContainer = true;
                NumberOfFrames = (int)stream->nb_frames;
            }
            else
            {
                NumberOfFrames = Duration.ToFrameNumber(stream->avg_frame_rate);
                if (IsVariableFrameRate)
                {
                    NumberOfFrames = null;
                }
            }
        }

        /// <summary>
        /// Gets the stream index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the codec name.
        /// </summary>
        public string CodecName { get; }

        /// <summary>
        /// Gets the codec identifier.
        /// </summary>
        public string CodecId { get; }

        /// <summary>
        /// Gets the stream's type.
        /// </summary>
        public MediaType Type { get; }

        /// <summary>
        /// Gets a value indicating whether the <see cref="FrameCount"/> value is know from the multimedia container metadata.
        /// </summary>
        public bool IsFrameCountProvidedByContainer { get; }

        /// <summary>
        /// Gets the stream time base.
        /// </summary>
        public AVRational TimeBase { get; }

        /// <summary>
        /// Gets the number of frames value taken from the container metadata or estimated in constant frame rate videos. Returns <see langword="null"/> if not available.
        /// </summary>
        public int? NumberOfFrames { get; }

        /// <summary>
        /// Gets the stream duration.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the stream start time. Null if undefined.
        /// </summary>
        public TimeSpan? StartTime { get; }

        /// <summary>
        /// Gets the average frame rate as a <see cref="double"/> value.
        /// </summary>
        public double AvgFrameRate { get; }

        /// <summary>
        /// Gets the frame rate as a <see cref="AVRational"/> value.
        /// It is used to calculate timestamps in the internal decoder methods.
        /// </summary>
        public AVRational RealFrameRate { get; }

        /// <summary>
        /// Gets a value indicating whether the video is variable frame rate (VFR).
        /// </summary>
        public bool IsVariableFrameRate { get; }

        /// <summary>
        /// Gets the stream metadata.
        /// </summary>
        public ReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Gets the duration of the stream in the time base units.
        /// </summary>
        internal long DurationRaw { get; }

        /// <summary>
        /// Creates the apprioriate type of <see cref="StreamInfo"/> class depending on the kind
        /// of stream passed in.
        /// </summary>
        /// <param name="stream">The represented stream.</param>
        /// <param name="owner">The input container.</param>
        /// <returns>The resulting new <see cref="StreamInfo"/> object.</returns>
        internal static unsafe StreamInfo Create(AVStream* stream, InputContainer owner)
        {
            var codec = stream->codec;
            if (codec->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                return new AudioStreamInfo(stream, owner);
            }

            if (codec->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                return new VideoStreamInfo(stream, owner);
            }

            return new StreamInfo(stream, MediaType.None, owner);
        }
    }
}
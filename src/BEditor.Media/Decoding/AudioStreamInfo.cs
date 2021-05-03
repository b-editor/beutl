using BEditor.Media.Audio;
using BEditor.Media.Common;
using BEditor.Media.Decoding.Internal;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents informations about the audio stream.
    /// </summary>
    public class AudioStreamInfo : StreamInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioStreamInfo"/> class.
        /// </summary>
        /// <param name="stream">A generic stream.</param>
        /// <param name="container">The input container.</param>
        internal unsafe AudioStreamInfo(AVStream* stream, InputContainer container) : base(stream, MediaType.Audio, container)
        {
            //var codec = stream->codecpar;
            //NumChannels = codec->channels;
            //SampleRate = codec->sample_rate;

            //SampleFormat = (SampleFormat)codec->format;
            //ChannelLayout = ffmpeg.av_get_default_channel_layout(codec->channels);
            var codec = stream->codec;
            NumChannels = codec->channels;
            SampleRate = codec->sample_rate;

            SampleFormat = (SampleFormat)codec->sample_fmt;
            ChannelLayout = ffmpeg.av_get_default_channel_layout(codec->channels);
        }

        /// <summary>
        /// Gets the number of audio channels stored in the stream.
        /// </summary>
        public int NumChannels { get; }

        /// <summary>
        /// Gets the number of samples per second of the audio stream.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets the audio sample format.
        /// </summary>
        public SampleFormat SampleFormat { get; }

        /// <summary>
        /// Gets the channel layout for this stream.
        /// </summary>
        internal long ChannelLayout { get; }
    }
}
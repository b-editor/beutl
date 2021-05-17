using System;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents informations about the audio stream.
    /// </summary>
    public sealed class AudioStreamInfo : StreamInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioStreamInfo"/> class.
        /// </summary>
        /// <param name="codecName">The codec name of the stream.</param>
        /// <param name="type">The media type of the stream.</param>
        /// <param name="duration">The duration of the stream.</param>
        /// <param name="samplerate">The number of samples per second of the audio stream.</param>
        public AudioStreamInfo(string codecName, MediaType type, TimeSpan duration, int samplerate) : base(codecName, type, duration)
        {
            SampleRate = samplerate;
        }

        /// <summary>
        /// Gets the number of samples per second of the audio stream.
        /// </summary>
        public int SampleRate { get; }
    }
}

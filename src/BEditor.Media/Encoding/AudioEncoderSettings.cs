using System.Collections.Generic;

using BEditor.Media.Audio;

using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents an audio encoder configuration.
    /// </summary>
    public class AudioEncoderSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioEncoderSettings"/> class with default video settings values.
        /// </summary>
        /// <param name="sampleRate">The sample rate of the stream.</param>
        /// <param name="channels">The number of channels in the stream.</param>
        /// <param name="codec">The audio encoder.</param>
        public AudioEncoderSettings(int sampleRate, int channels, AudioCodec codec = AudioCodec.Default, SampleFormat format = SampleFormat.SignedWord)
        {
            SampleRate = sampleRate;
            Channels = channels;
            Codec = codec;
            SampleFormat = format;
            CodecOptions = new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets or sets the audio stream bitrate (bytes per second). The default value is 128,000 B/s.
        /// </summary>
        public int Bitrate { get; set; } = 128_000;

        /// <summary>
        /// Gets or sets the audio stream sample rate (samples per second). The default value is 44,100 samples/sec.
        /// </summary>
        public int SampleRate { get; set; } = 44_100;

        /// <summary>
        /// Gets or sets the number of channels in the audio stream. The default value is 2.
        /// </summary>
        public int Channels { get; set; } = 2;

        /// <summary>
        /// Gets or sets the number of samples per audio frame. Default is 2205 (1/20th of a second at 44.1kHz).
        /// </summary>
        public int SamplesPerFrame { get; set; } = 2205;

        /// <summary>
        /// Gets or the time base of the audio stream. Always equal to <see cref="SamplesPerFrame"/>/<see cref="SampleRate"/>.
        /// </summary>
        public AVRational TimeBase => new() { num = SamplesPerFrame, den = SampleRate };

        /// <summary>
        /// Gets or sets the sample format to be used by the audio codec. The default value is <see cref="SampleFormat.SignedWord"/> (16-bit integer).
        /// </summary>
        public SampleFormat SampleFormat { get; set; }

        /// <summary>
        /// Gets or sets the dictionary with custom codec options.
        /// </summary>
        public Dictionary<string, string> CodecOptions { get; set; }

        /// <summary>
        /// Gets or sets the codec for this stream.
        /// If set to <see cref="AudioCodec.Default"/>, encoder will use default audio codec for current container.
        /// </summary>
        public AudioCodec Codec { get; set; }
    }
}
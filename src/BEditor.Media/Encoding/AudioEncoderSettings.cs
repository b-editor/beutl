// AudioEncoderSettings.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

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
        public AudioEncoderSettings(int sampleRate, int channels)
        {
            SampleRate = sampleRate;
            Channels = channels;
            CodecOptions = new();
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
        /// Gets or sets the dictionary with custom codec options.
        /// </summary>
        public Dictionary<string, object> CodecOptions { get; set; }
    }
}
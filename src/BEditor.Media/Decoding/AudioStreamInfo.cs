// AudioStreamInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        /// <param name="numchannels">The number of audio channels stored in the stream.</param>
        public AudioStreamInfo(string codecName, MediaType type, TimeSpan duration, int samplerate, int numchannels)
            : base(codecName, type, duration)
        {
            SampleRate = samplerate;
            NumChannels = numchannels;
        }

        /// <summary>
        /// Gets the number of samples per second of the audio stream.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets the number of audio channels stored in the stream.
        /// </summary>
        public int NumChannels { get; }
    }
}
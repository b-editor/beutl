// IAudioStream.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents an audio stream in the <see cref="MediaFile"/>.
    /// </summary>
    public interface IAudioStream : IMediaStream
    {
        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public new AudioStreamInfo Info { get; }

        /// <inheritdoc/>
        StreamInfo IMediaStream.Info => Info;

        /// <summary>
        /// Reads the next frame from the audio stream.
        /// </summary>
        /// <returns>The decoded audio data.</returns>
        public Sound<StereoPCMFloat> GetNextFrame();

        /// <summary>
        /// Reads the next frame from the audio stream.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="sound">The decoded audio data.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetNextFrame([NotNullWhen(true)] out Sound<StereoPCMFloat>? sound);

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <returns>The decoded audio frame.</returns>
        public Sound<StereoPCMFloat> GetFrame(TimeSpan time);

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="samples">The audio duration.</param>
        /// <returns>The decoded audio frame.</returns>
        public Sound<StereoPCMFloat> GetFrame(TimeSpan time, int samples)
        {
            var sound = new Sound<StereoPCMFloat>(Info.SampleRate, samples);
            if (TryGetFrame(time, out var first))
            {
                // デコードしたサンプル数
                var decoded = first.NumSamples;
                first.Data.CopyTo(sound.Data.Slice(0, first.NumSamples));
                first.Dispose();

                while (decoded < samples && TryGetNextFrame(out var data))
                {
                    data.Data.CopyTo(sound.Data.Slice(decoded, data.NumSamples));

                    decoded += data.NumSamples;

                    data.Dispose();
                }
            }

            return sound;
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="duration">The audio duration.</param>
        /// <returns>The decoded audio frame.</returns>
        public Sound<StereoPCMFloat> GetFrame(TimeSpan time, TimeSpan duration)
        {
            var sound = new Sound<StereoPCMFloat>(Info.SampleRate, duration);
            if (TryGetFrame(time, out var first))
            {
                // デコードしたサンプル数
                var decoded = first.NumSamples;
                first.Data.CopyTo(sound.Data.Slice(0, first.NumSamples));
                first.Dispose();

                while (decoded < sound.NumSamples && TryGetNextFrame(out var data))
                {
                    data.Data.CopyTo(sound.Data.Slice(decoded, data.NumSamples));

                    decoded += data.NumSamples;

                    data.Dispose();
                }
            }

            return sound;
        }

        /// <summary>
        /// Reads the audio data found at the specified timestamp.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="sound">The decoded audio data.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetFrame(TimeSpan time, [NotNullWhen(true)] out Sound<StereoPCMFloat>? sound);
    }
}
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
        StreamInfo IMediaStream.Info => Info;

        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public new AudioStreamInfo Info { get; }

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
        /// <returns>The decoded video frame.</returns>
        public Sound<StereoPCMFloat> GetFrame(TimeSpan time);

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
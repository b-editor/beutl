using System;
using System.IO;

using BEditor.Media.Audio;
using BEditor.Media.Common.Internal;
using BEditor.Media.Decoding.Internal;
using BEditor.Media.PCM;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents an audio stream in the <see cref="MediaFile"/>.
    /// </summary>
    public unsafe class AudioStream : MediaStream
    {
        private readonly SwrContext* _swrContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioStream"/> class.
        /// </summary>
        /// <param name="stream">The audio stream.</param>
        /// <param name="options">The decoder settings.</param>
        internal AudioStream(Decoder stream, MediaOptions options) : base(stream, options)
        {
            _swrContext = ffmpeg.swr_alloc_set_opts(
                null,
                Info.ChannelLayout,
                (AVSampleFormat)SampleFormat.SingleP,
                Info.SampleRate,
                Info.ChannelLayout,
                (AVSampleFormat)Info.SampleFormat,
                Info.SampleRate,
                0,
                null);

            ffmpeg.swr_init(_swrContext);
        }

        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public new AudioStreamInfo Info => (AudioStreamInfo)base.Info;

        /// <summary>
        /// Reads the next frame from the audio stream.
        /// </summary>
        /// <returns>The decoded audio data.</returns>
        public new AudioData GetNextFrame()
        {
            var frame = (AudioFrame)base.GetNextFrame();

            var converted = AudioFrame.Create(
                frame.SampleRate,
                frame.NumChannels,
                frame.NumSamples,
                frame.ChannelLayout,
                SampleFormat.SingleP,
                frame.DecodingTimestamp,
                frame.PresentationTimestamp);

            ffmpeg.swr_convert_frame(_swrContext, converted.Pointer, frame.Pointer);

            return new AudioData(converted);
        }

        /// <summary>
        /// Reads the next frame from the audio stream.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="data">The decoded audio data.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetNextFrame(out AudioData data)
        {
            try
            {
                data = GetNextFrame();
                return true;
            }
            catch (EndOfStreamException)
            {
                data = default;
                return false;
            }
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <returns>The decoded video frame.</returns>
        public new AudioData GetFrame(TimeSpan time)
        {
            var frame = (AudioFrame)base.GetFrame(time);
            return new AudioData(frame);
        }

        /// <summary>
        /// Reads the video frames found with the specified length from the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="length">The frame length.</param>
        /// <returns>The decoded video frame.</returns>
        public Sound<StereoPCMFloat> GetFrame(TimeSpan time, int length)
        {
            var sound = new Sound<StereoPCMFloat>(Info.SampleRate, length);
            var first = GetFrame(time);
            // デコードしたサンプル数
            var decoded = first.NumSamples;
            for (uint c = 0; c < Info.NumChannels; c++)
            {
                sound.SetChannelData((int)c, first.GetChannelData(c));
            }

            while (decoded <= length)
            {
                var data = GetNextFrame();

                for (uint c = 0; c < Info.NumChannels; c++)
                {
                    sound.SetChannelData(decoded, (int)c, data.GetChannelData(c));
                }

                decoded += data.NumSamples;
            }

            return sound;
        }

        /// <summary>
        /// Reads the audio data found at the specified timestamp.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="data">The decoded audio data.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        public bool TryGetFrame(TimeSpan time, out AudioData data)
        {
            try
            {
                data = GetFrame(time);
                return true;
            }
            catch (EndOfStreamException)
            {
                data = default;
                return false;
            }
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
#pragma warning disable RCS1176
            fixed (SwrContext** ptr = &_swrContext)
            {
                ffmpeg.swr_free(ptr);
            }

            base.Dispose();
            GC.SuppressFinalize(this);
#pragma warning restore RCS1176
        }
    }
}
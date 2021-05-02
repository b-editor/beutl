using System;

using BEditor.Media.Audio;
using BEditor.Media.Common.Internal;
using BEditor.Media.Encoding.Internal;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents an audio encoder stream.
    /// </summary>
    public unsafe class AudioOutputStream : IDisposable
    {
        private readonly OutputStream<AudioFrame> _stream;
        private readonly AudioFrame _frame;
        private readonly SwrContext* _swrContext;
        private bool _isDisposed;
        private long _lastFramePts = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioOutputStream"/> class.
        /// </summary>
        /// <param name="stream">The audio stream.</param>
        /// <param name="config">The stream setting.</param>
        internal AudioOutputStream(OutputStream<AudioFrame> stream, AudioEncoderSettings config)
        {
            _stream = stream;

            var channelLayout = ffmpeg.av_get_default_channel_layout(config.Channels);
            _swrContext = ffmpeg.swr_alloc_set_opts(
                null,
                channelLayout,
                (AVSampleFormat)config.SampleFormat,
                config.SampleRate,
                channelLayout,
                (AVSampleFormat)SampleFormat.SingleP,
                config.SampleRate,
                0,
                null);

            ffmpeg.swr_init(_swrContext);

            Configuration = config;
            _frame = AudioFrame.Create(config.SampleRate, config.Channels, config.SamplesPerFrame, channelLayout, SampleFormat.SingleP);
        }

        /// <summary>
        /// Gets the video encoding configuration used to create this stream.
        /// </summary>
        public AudioEncoderSettings Configuration { get; }

        /// <summary>
        /// Gets the current duration of this stream.
        /// </summary>
        public TimeSpan CurrentDuration => _lastFramePts.ToTimeSpan(Configuration.TimeBase);

        /// <summary>
        /// Writes the specified audio data to the stream as the next frame.
        /// </summary>
        /// <param name="data">The audio data to write.</param>
        /// <param name="customPtsValue">(optional) custom PTS value for the frame.</param>
        public void AddFrame(AudioData data, long customPtsValue)
        {
            if (customPtsValue <= _lastFramePts)
            {
                throw new Exception("Cannot add a frame that occurs chronologically before the most recently written frame!");
            }

            _frame.UpdateFromAudioData(data);

            var converted = AudioFrame.Create(
                _frame.SampleRate,
                _frame.NumChannels,
                _frame.NumSamples,
                _frame.ChannelLayout,
                Configuration.SampleFormat);
            converted.PresentationTimestamp = customPtsValue;

            ffmpeg.swr_convert_frame(_swrContext, converted.Pointer, _frame.Pointer);

            _stream.Push(converted);
            converted.Dispose();

            _lastFramePts = customPtsValue;
        }

        /// <summary>
        /// Writes the specified sample data to the stream as the next frame.
        /// </summary>
        /// <param name="samples">The sample data to write.</param>
        /// <param name="customPtsValue">(optional) custom PTS value for the frame.</param>
        public void AddFrame(float[][] samples, long customPtsValue)
        {
            if (customPtsValue <= _lastFramePts)
            {
                throw new Exception("Cannot add a frame that occurs chronologically before the most recently written frame!");
            }

            _frame.UpdateFromSampleData(samples);
            _frame.PresentationTimestamp = customPtsValue;
            _stream.Push(_frame);

            _lastFramePts = customPtsValue;
        }

        /// <summary>
        /// Writes the specified audio data to the stream as the next frame.
        /// </summary>
        /// <param name="data">The audio data to write.</param>
        /// <param name="customTime">Custom timestamp for this frame.</param>
        public void AddFrame(AudioData data, TimeSpan customTime) => AddFrame(data, customTime.ToTimestamp(Configuration.TimeBase));

        /// <summary>
        /// Writes the specified audio data to the stream as the next frame.
        /// </summary>
        /// <param name="data">The audio data to write.</param>
        public void AddFrame(AudioData data) => AddFrame(data, _lastFramePts + 1);

        /// <summary>
        /// Writes the specified sample data to the stream as the next frame.
        /// </summary>
        /// <param name="samples">The sample data to write.</param>
        /// <param name="customTime">Custom timestamp for this frame.</param>
        public void AddFrame(float[][] samples, TimeSpan customTime) => AddFrame(samples, customTime.ToTimestamp(Configuration.TimeBase));

        /// <summary>
        /// Writes the specified sample data to the stream as the next frame.
        /// </summary>
        /// <param name="samples">The sample data to write.</param>
        public void AddFrame(float[][] samples) => AddFrame(samples, _lastFramePts + 1);

#pragma warning disable RCS1176
        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _stream.Dispose();
            _frame.Dispose();

            fixed (SwrContext** ptr = &_swrContext)
            {
                ffmpeg.swr_free(ptr);
            }

            GC.SuppressFinalize(this);
            _isDisposed = true;
        }
#pragma warning restore RCS1176
    }
}

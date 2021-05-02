using System;

using BEditor.Drawing;

using BEditor.Media.Common.Internal;
using BEditor.Media.Encoding.Internal;
using BEditor.Media.Graphics;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents a video encoder stream.
    /// </summary>
    public class VideoOutputStream : IDisposable
    {
        private readonly OutputStream<VideoFrame> _stream;
        private readonly VideoFrame _encodedFrame;
        private readonly ImageConverter _converter;
        private bool _isDisposed;
        private long _lastFramePts = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoOutputStream"/> class.
        /// </summary>
        /// <param name="stream">The video stream.</param>
        /// <param name="config">The stream setting.</param>
        internal VideoOutputStream(OutputStream<VideoFrame> stream, VideoEncoderSettings config)
        {
            _stream = stream;
            Configuration = config;
            _converter = new ImageConverter();

            var (size, format) = GetStreamLayout(stream);
            _encodedFrame = VideoFrame.Create(size, format);
        }

        /// <summary>
        /// Gets the video encoding configuration used to create this stream.
        /// </summary>
        public VideoEncoderSettings Configuration { get; }

        /// <summary>
        /// Gets the current duration of this stream.
        /// </summary>
        public TimeSpan CurrentDuration => _lastFramePts.ToTimeSpan(Configuration.TimeBase);

        /// <summary>
        /// Writes the specified bitmap to the video stream as the next frame.
        /// </summary>
        /// <param name="frame">The bitmap to write.</param>
        /// <param name="customPtsValue">(optional) custom PTS value for the frame.</param>
        public void AddFrame(ImageData frame, long customPtsValue)
        {
            if (customPtsValue <= _lastFramePts)
            {
                throw new Exception("Cannot add a frame that occurs chronologically before the most recently written frame!");
            }

            _encodedFrame.UpdateFromBitmap(frame, _converter);
            _encodedFrame.PresentationTimestamp = customPtsValue;
            _stream.Push(_encodedFrame);

            _lastFramePts = customPtsValue;
        }

        /// <summary>
        /// Writes the specified bitmap to the video stream as the next frame.
        /// </summary>
        /// <param name="frame">The bitmap to write.</param>
        /// <param name="customTime">Custom timestamp for this frame.</param>
        public void AddFrame(ImageData frame, TimeSpan customTime)
        {
            AddFrame(frame, customTime.ToTimestamp(Configuration.TimeBase));
        }

        /// <summary>
        /// Writes the specified bitmap to the video stream as the next frame.
        /// </summary>
        /// <param name="frame">The bitmap to write.</param>
        public void AddFrame(ImageData frame)
        {
            AddFrame(frame, _lastFramePts + 1);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _stream.Dispose();
            _encodedFrame.Dispose();
            _converter.Dispose();

            GC.SuppressFinalize(this);
            _isDisposed = true;
        }

        private static unsafe (Size, AVPixelFormat) GetStreamLayout(OutputStream<VideoFrame> videoStream)
        {
            var codec = videoStream.Pointer->codec;
            return (new Size(codec->width, codec->height), codec->pix_fmt);
        }
    }
}

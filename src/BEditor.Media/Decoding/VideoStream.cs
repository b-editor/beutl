using System;
using System.IO;

using BEditor.Drawing;
using BEditor.Media.Common.Internal;
using BEditor.Media.Decoding.Internal;
using BEditor.Media.Graphics;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a video stream in the <see cref="MediaFile"/>.
    /// </summary>
    public class VideoStream : MediaStream
    {
        private readonly int _outputFrameStride;
        private readonly int _requiredBufferSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoStream"/> class.
        /// </summary>
        /// <param name="stream">The video stream.</param>
        /// <param name="options">The decoder settings.</param>
        internal VideoStream(Decoder stream, MediaOptions options): base(stream, options)
        {
            OutputFrameSize = options.TargetVideoSize ?? Info.FrameSize;
            Converter = new Lazy<ImageConverter>(() => new ImageConverter(Info.FrameSize, Info.AVPixelFormat, OutputFrameSize, (AVPixelFormat)options.VideoPixelFormat));

            _outputFrameStride = ImageData.EstimateStride(OutputFrameSize.Width, Options.VideoPixelFormat);
            _requiredBufferSize = _outputFrameStride * OutputFrameSize.Height;
        }

        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public new VideoStreamInfo Info => (VideoStreamInfo)base.Info;

        private Lazy<ImageConverter> Converter { get; }

        private Size OutputFrameSize { get; }

        /// <summary>
        /// Reads the next frame from the video stream.
        /// </summary>
        /// <returns>A decoded bitmap.</returns>
        /// <exception cref="EndOfStreamException">End of the stream.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public new ImageData GetNextFrame()
        {
            var frame = (VideoFrame)base.GetNextFrame();
            return frame.ToBitmap(Converter.Value, Options.VideoPixelFormat, OutputFrameSize);
        }

        /// <summary>
        /// Reads the next frame from the video stream.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="bitmap">The decoded video frame.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public bool TryGetNextFrame(out ImageData bitmap)
        {
            try
            {
                bitmap = GetNextFrame();
                return true;
            }
            catch (EndOfStreamException)
            {
                bitmap = default;
                return false;
            }
        }

        /// <summary>
        /// Reads the next frame from the video stream  and writes the converted bitmap data directly to the provided buffer.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="buffer">Pointer to the memory buffer.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="ArgumentException">Too small buffer.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public unsafe bool TryGetNextFrame(Span<byte> buffer)
        {
            if (buffer.Length < _requiredBufferSize)
            {
                throw new ArgumentException("Destination buffer is smaller than the converted bitmap data.", nameof(buffer));
            }

            try
            {
                fixed (byte* ptr = buffer)
                {
                    ConvertCopyFrameToMemory((VideoFrame)base.GetNextFrame(), ptr);
                }

                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the next frame from the video stream  and writes the converted bitmap data directly to the provided buffer.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="buffer">Pointer to the memory buffer.</param>
        /// <param name="bufferStride">Size in bytes of a single row of pixels.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="ArgumentException">Too small buffer.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public unsafe bool TryGetNextFrame(IntPtr buffer, int bufferStride)
        {
            if (bufferStride != _outputFrameStride)
            {
                throw new ArgumentException("Destination buffer is smaller than the converted bitmap data.", nameof(bufferStride));
            }

            try
            {
                ConvertCopyFrameToMemory((VideoFrame)base.GetNextFrame(), (byte*)buffer);
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <returns>The decoded video frame.</returns>
        /// <exception cref="EndOfStreamException">End of the stream.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public new ImageData GetFrame(TimeSpan time)
        {
            var frame = (VideoFrame)base.GetFrame(time);
            return frame.ToBitmap(Converter.Value, Options.VideoPixelFormat, OutputFrameSize);
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="bitmap">The decoded video frame.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public bool TryGetFrame(TimeSpan time, out ImageData bitmap)
        {
            try
            {
                bitmap = GetFrame(time);
                return true;
            }
            catch (EndOfStreamException)
            {
                bitmap = default;
                return false;
            }
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp and writes the converted bitmap data directly to the provided buffer.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="buffer">Pointer to the memory buffer.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="ArgumentException">Too small buffer.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public unsafe bool TryGetFrame(TimeSpan time, Span<byte> buffer)
        {
            if (buffer.Length < _requiredBufferSize)
            {
                throw new ArgumentException("Destination buffer is smaller than the converted bitmap data.", nameof(buffer));
            }

            try
            {
                fixed (byte* ptr = buffer)
                {
                    ConvertCopyFrameToMemory((VideoFrame)base.GetFrame(time), ptr);
                }

                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the video frame found at the specified timestamp and writes the converted bitmap data directly to the provided buffer.
        /// A <see langword="false"/> return value indicates that reached end of stream.
        /// The method throws exception if another error has occurred.
        /// </summary>
        /// <param name="time">The frame timestamp.</param>
        /// <param name="buffer">Pointer to the memory buffer.</param>
        /// <param name="bufferStride">Size in bytes of a single row of pixels.</param>
        /// <returns><see langword="false"/> if reached end of the stream.</returns>
        /// <exception cref="ArgumentException">Too small buffer.</exception>
        /// <exception cref="FFmpegException">Internal decoding error.</exception>
        public unsafe bool TryGetFrame(TimeSpan time, IntPtr buffer, int bufferStride)
        {
            if (bufferStride != _outputFrameStride)
            {
                throw new ArgumentException("Destination buffer is smaller than the converted bitmap data.", nameof(bufferStride));
            }

            try
            {
                ConvertCopyFrameToMemory((VideoFrame)base.GetFrame(time), (byte*)buffer);
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        private unsafe void ConvertCopyFrameToMemory(VideoFrame frame, byte* target)
        {
            Converter.Value.AVFrameToBitmap(frame, target, _outputFrameStride);
        }
    }
}

using System;

using BEditor.Drawing;

using BEditor.Media.Graphics;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Common.Internal
{
    /// <summary>
    /// Represent a video frame.
    /// </summary>
    internal unsafe class VideoFrame : MediaFrame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFrame"/> class with empty frame data.
        /// </summary>
        public VideoFrame() : base(ffmpeg.av_frame_alloc())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFrame"/> class using existing <see cref="AVFrame"/>.
        /// </summary>
        /// <param name="frame">The video <see cref="AVFrame"/>.</param>
        public VideoFrame(AVFrame* frame) : base(frame)
        {
            if (frame->GetMediaType() == MediaType.Audio)
            {
                throw new ArgumentException("Cannot create a VideoFrame instance from the AVFrame containing audio.");
            }
        }

        /// <summary>
        /// Gets the frame dimensions.
        /// </summary>
        public Size Layout => Pointer != null ? new Size(Pointer->width, Pointer->height) : default;

        /// <summary>
        /// Gets the frame pixel format.
        /// </summary>
        public AVPixelFormat PixelFormat => Pointer != null ? (AVPixelFormat)Pointer->format : default;

        /// <summary>
        /// Creates a video frame with given dimensions and allocates a buffer for it.
        /// </summary>
        /// <param name="dimensions">The dimensions of the video frame.</param>
        /// <param name="pixelFormat">The video pixel format.</param>
        /// <returns>The new video frame.</returns>
        public static VideoFrame Create(Size dimensions, AVPixelFormat pixelFormat)
        {
            var frame = ffmpeg.av_frame_alloc();

            frame->width = dimensions.Width;
            frame->height = dimensions.Height;
            frame->format = (int)pixelFormat;

            ffmpeg.av_frame_get_buffer(frame, 32);

            return new(frame);
        }

        /// <summary>
        /// Creates an empty frame for decoding.
        /// </summary>
        /// <returns>The empty <see cref="VideoFrame"/>.</returns>
        public static VideoFrame CreateEmpty()
        {
            return new();
        }

        /// <summary>
        /// Overrides this video frame data with the converted <paramref name="bitmap"/> using specified <see cref="ImageConverter"/> object.
        /// </summary>
        /// <param name="bitmap">The bitmap to convert.</param>
        /// <param name="converter">A <see cref="ImageConverter"/> object, used for caching the FFMpeg <see cref="SwsContext"/> when converting many frames of the same video.</param>
        public void UpdateFromBitmap(ImageData bitmap, ImageConverter converter)
        {
            converter.FillAVFrame(bitmap, this);
        }

        /// <summary>
        /// Converts this video frame to the <see cref="ImageData"/> with the specified pixel format.
        /// </summary>
        /// <param name="converter">A <see cref="ImageConverter"/> object, used for caching the FFMpeg <see cref="SwsContext"/> when converting many frames of the same video.</param>
        /// <param name="targetFormat">The output bitmap pixel format.</param>
        /// /// <param name="targetSize">The output bitmap size.</param>
        /// <returns>A <see cref="ImageData"/> instance containing converted bitmap data.</returns>
        public ImageData ToBitmap(ImageConverter converter, ImagePixelFormat targetFormat, Size targetSize)
        {
            var bitmap = ImageData.CreatePooled(targetSize, targetFormat); // Rents memory for the output bitmap.

            fixed (byte* ptr = bitmap.Data)
            {
                // Converts the raw video frame using the given size and pixel format and writes it to the ImageData bitmap.
                converter.AVFrameToBitmap(this, ptr, bitmap.Stride);
            }

            return bitmap;
        }

        /// <inheritdoc/>
        internal override void Update(AVFrame* newFrame)
        {
            if (newFrame->GetMediaType() != MediaType.Video)
            {
                throw new ArgumentException("The new frame doesn't contain video data.");
            }

            base.Update(newFrame);
        }
    }
}
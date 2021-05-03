using BEditor.Drawing;

using BEditor.Media.Graphics;

using FFmpeg.AutoGen;

namespace BEditor.Media.Common.Internal
{
    /// <summary>
    /// A class used to convert ffmpeg <see cref="AVFrame"/>s to <see cref="ImageData"/> objects with specified image size and color format.
    /// </summary>
    internal unsafe class ImageConverter : Wrapper<SwsContext>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ImageConverter"/> class.
        /// This constructor should be used only for video encoding!.
        /// </summary>
        public ImageConverter() : base(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageConverter"/> class.
        /// </summary>
        /// <param name="sourceSize">Source image size.</param>
        /// <param name="sourceFormat">Source image format.</param>
        /// <param name="destinationSize">Destination image size.</param>
        /// <param name="destinationFormat">Destination image format.</param>
        public ImageConverter(Size sourceSize, AVPixelFormat sourceFormat, Size destinationSize, AVPixelFormat destinationFormat) : base(null)
        {
            var scaleMode = sourceSize == destinationSize ? ffmpeg.SWS_POINT : ffmpeg.SWS_BICUBIC;
            UpdatePointer(ffmpeg.sws_getContext(sourceSize.Width, sourceSize.Height, sourceFormat, destinationSize.Width, destinationSize.Height, destinationFormat, scaleMode, null, null, null));

            if (Pointer == null)
            {
                throw new FFmpegException("Cannot allocate a SwsContext to convert the video frame. The ffmpeg.sws_getContext method returned \"null\"");
            }
        }

        /// <summary>
        /// Overrides the <paramref name="destinationFrame"/> image buffer with the converted <see cref="ImageData"/> bitmap. Used in encoding.
        /// </summary>
        /// <param name="bitmap">The input bitmap.</param>
        /// <param name="destinationFrame">The <see cref="AVFrame"/> to override.</param>
        internal void FillAVFrame(ImageData bitmap, VideoFrame destinationFrame)
        {
            UpdateContext(bitmap.ImageSize, (AVPixelFormat)bitmap.PixelFormat, destinationFrame.Layout, destinationFrame.PixelFormat);

            fixed (byte* ptr = bitmap.Data)
            {
                var data = new byte*[4] { ptr, null, null, null };
                var linesize = new int[4] { bitmap.Stride, 0, 0, 0 };

                ffmpeg.sws_scale(Pointer, data, linesize, 0, bitmap.ImageSize.Height, destinationFrame.Pointer->data, destinationFrame.Pointer->linesize);
            }
        }

        /// <summary>
        /// Converts a video <see cref="AVFrame"/> to the specified <see cref="ImageData"/> bitmap. Used in decoding.
        /// </summary>
        /// <param name="videoFrame">The video frame to convert.</param>
        /// <param name="destination">The destination <see cref="ImageData"/>.</param>
        /// <param name="stride">Size of the single bitmap row.</param>
        internal void AVFrameToBitmap(VideoFrame videoFrame, byte* destination, int stride)
        {
            var data = new byte*[4] { destination, null, null, null };
            var linesize = new int[4] { stride, 0, 0, 0 };

            ffmpeg.sws_scale(Pointer, videoFrame.Pointer->data, videoFrame.Pointer->linesize, 0, videoFrame.Layout.Height, data, linesize);
        }

        /// <inheritdoc/>
        protected override void OnDisposing() => ffmpeg.sws_freeContext(Pointer);

        private void UpdateContext(Size sourceSize, AVPixelFormat sourceFormat, Size destinationSize, AVPixelFormat destinationFormat)
        {
            var scaleMode = sourceSize == destinationSize ? ffmpeg.SWS_POINT : ffmpeg.SWS_BICUBIC;
            var newPointer = ffmpeg.sws_getCachedContext(Pointer, sourceSize.Width, sourceSize.Height, sourceFormat, destinationSize.Width, destinationSize.Height, destinationFormat, scaleMode, null, null, null);

            if (newPointer == null)
            {
                throw new FFmpegException("Cannot get a SwsContext to convert the bitmap image. The ffmpeg.sws_getCachedContext method returned \"null\"");
            }

            UpdatePointer(newPointer);
        }
    }
}
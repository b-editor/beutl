using System;
using System.Buffers;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Media.Graphics
{
    /// <summary>
    /// Represent a lightweight container for bitmap images.
    /// </summary>
    public ref struct ImageData
    {
        private readonly Span<byte> _span;
        private readonly IMemoryOwner<byte>? _pooledMemory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageData"/> struct using a <see cref="Span{T}"/> as the data source.
        /// </summary>
        /// <param name="data">The bitmap data.</param>
        /// <param name="pixelFormat">The pixel format.</param>
        /// <param name="imageSize">The image dimensions.</param>
        /// <exception cref="ArgumentException">When data span size doesn't match size calculated from width, height and the pixel format.</exception>
        public ImageData(Span<byte> data, ImagePixelFormat pixelFormat, Size imageSize)
        {
            var size = EstimateStride(imageSize.Width, pixelFormat) * imageSize.Height;
            if (data.Length != size)
            {
                throw new ArgumentException("Pixel buffer size doesn't match size required by this image format.");
            }

            _span = data;
            _pooledMemory = null;

            ImageSize = imageSize;
            PixelFormat = pixelFormat;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageData"/> struct using a <see cref="Span{T}"/> as the data source.
        /// </summary>
        /// <param name="data">The bitmap data.</param>
        /// <param name="pixelFormat">The pixel format.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <exception cref="ArgumentException">When data span size doesn't match size calculated from width, height and the pixel format.</exception>
        public ImageData(Span<byte> data, ImagePixelFormat pixelFormat, int width, int height)
            : this(data, pixelFormat, new Size(width, height))
        {
        }

        private ImageData(IMemoryOwner<byte> memory, Size size, ImagePixelFormat pixelFormat)
        {
            _span = null;
            _pooledMemory = memory;

            ImageSize = size;
            PixelFormat = pixelFormat;
        }

        /// <summary>
        /// Gets the <see cref="Span{T}"/> object containing the bitmap data.
        /// </summary>
        public Span<byte> Data => _pooledMemory != null ? _pooledMemory.Memory.Span : _span;

        /// <summary>
        /// Gets a value indicating whether this instance of <see cref="ImageData"/> uses memory pooling.
        /// </summary>
        public bool IsPooled => _pooledMemory != null;

        /// <summary>
        /// Gets the image size.
        /// </summary>
        public Size ImageSize { get; }

        /// <summary>
        /// Gets the bitmap pixel format.
        /// </summary>
        public ImagePixelFormat PixelFormat { get; }

        /// <summary>
        /// Gets the estimated number of bytes in one row of image pixels.
        /// </summary>
        public int Stride => EstimateStride(ImageSize.Width, PixelFormat);

        /// <summary>
        /// Rents a memory buffer from pool and creates a new instance of <see cref="ImageData"/> class from it.
        /// </summary>
        /// <param name="imageSize">The image dimensions.</param>
        /// <param name="pixelFormat">The bitmap pixel format.</param>
        /// <returns>The new <see cref="ImageData"/> instance.</returns>
        public static ImageData CreatePooled(Size imageSize, ImagePixelFormat pixelFormat)
        {
            var size = EstimateStride(imageSize.Width, pixelFormat) * imageSize.Height;
            var pool = MemoryPool<byte>.Shared;
            var memory = pool.Rent(size);
            return new ImageData(memory, imageSize, pixelFormat);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class using a byte array as the data source.
        /// </summary>
        /// <param name="pixels">The byte array containing bitmap data.</param>
        /// <param name="pixelFormat">The bitmap pixel format.</param>
        /// <param name="imageSize">The image dimensions.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static ImageData FromArray(byte[] pixels, ImagePixelFormat pixelFormat, Size imageSize)
        {
            return new(new Span<byte>(pixels), pixelFormat, imageSize);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class using a byte array as the data source.
        /// </summary>
        /// <param name="pixels">The byte array containing bitmap data.</param>
        /// <param name="pixelFormat">The bitmap pixel format.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static ImageData FromArray(byte[] pixels, ImagePixelFormat pixelFormat, int width, int height)
        {
            return FromArray(pixels, pixelFormat, new Size(width, height));
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class using a pointer to the unmanaged memory as the data source.
        /// </summary>
        /// <param name="pointer">The byte array containing bitmap data.</param>
        /// <param name="pixelFormat">The bitmap pixel format.</param>
        /// <param name="imageSize">The image dimensions.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static ImageData FromPointer(IntPtr pointer, ImagePixelFormat pixelFormat, Size imageSize)
        {
            var span = CreateSpan(pointer, imageSize, pixelFormat);
            return new ImageData(span, pixelFormat, imageSize);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class using a pointer to the unmanaged memory as the data source.
        /// </summary>
        /// <param name="pointer">The byte array containing bitmap data.</param>
        /// <param name="pixelFormat">The bitmap pixel format.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static ImageData FromPointer(IntPtr pointer, ImagePixelFormat pixelFormat, int width, int height)
        {
            return FromPointer(pointer, pixelFormat, new Size(width, height));
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class, using <see cref="Image{T}"/> as the data source.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static unsafe ImageData FromDrawing(Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                return FromPointer((IntPtr)data, ImagePixelFormat.Bgra32, image.Width, image.Height);
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class, using <see cref="Image{T}"/> as the data source.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static unsafe ImageData FromDrawing(Image<BGR24> image)
        {
            fixed (BGR24* data = image.Data)
            {
                return FromPointer((IntPtr)data, ImagePixelFormat.Bgr24, image.Width, image.Height);
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class, using <see cref="Image{T}"/> as the data source.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static unsafe ImageData FromDrawing(Image<RGBA32> image)
        {
            fixed (RGBA32* data = image.Data)
            {
                return FromPointer((IntPtr)data, ImagePixelFormat.Rgba32, image.Width, image.Height);
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ImageData"/> class, using <see cref="Image{T}"/> as the data source.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <returns>A new <see cref="ImageData"/> instance.</returns>
        public static unsafe ImageData FromDrawing(Image<RGB24> image)
        {
            fixed (RGB24* data = image.Data)
            {
                return FromPointer((IntPtr)data, ImagePixelFormat.Rgb24, image.Width, image.Height);
            }
        }

        /// <summary>
        /// Gets the estimated image line size based on the pixel format and width.
        /// </summary>
        /// <param name="width">The image width.</param>
        /// <param name="format">The image pixel format.</param>
        /// <returns>The size of a single line of the image measured in bytes.</returns>
        public static int EstimateStride(int width, ImagePixelFormat format)
        {
            return 4 * (((GetBitsPerPixel(format) * width) + 31) / 32);
        }

        /// <summary>
        /// Convert this <see cref="ImageData"/> to <see cref="Image{T}"/>.
        /// </summary>
        /// <exception cref="Exception">Cannot convert ImageData to Image&lt;BGRA32&gt; from ImageData with PixelFormat other than ImagePixelFormat.Bgra32.</exception>
        /// <returns>A new <see cref="Image{T}"/> instance.</returns>
        public unsafe Image<BGRA32> ToDrawing()
        {
            if (PixelFormat != ImagePixelFormat.Bgra32)
            {
                throw new Exception("Cannot convert ImageData to Image<BGRA32> from ImageData with PixelFormat other than ImagePixelFormat.Bgra32.");
            }

            var image = new Image<BGRA32>(ImageSize.Width, ImageSize.Height);
            fixed (byte* src = Data)
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy(src, dst, size, size);
            }

            return image;
        }

        private static unsafe Span<byte> CreateSpan(IntPtr pointer, Size imageSize, ImagePixelFormat pixelFormat)
        {
            var size = EstimateStride(imageSize.Width, pixelFormat) * imageSize.Height;
            return new Span<byte>((void*)pointer, size);
        }

        private static int GetBitsPerPixel(ImagePixelFormat format)
        {
            return format switch
            {
                ImagePixelFormat.Bgr24 => 24,
                ImagePixelFormat.Bgra32 => 32,
                ImagePixelFormat.Rgb24 => 24,
                ImagePixelFormat.Rgba32 => 32,
                ImagePixelFormat.Argb32 => 32,
                ImagePixelFormat.Uyvy422 => 16,
                ImagePixelFormat.Yuv420 => 12,
                ImagePixelFormat.Yuv422 => 16,
                ImagePixelFormat.Yuv444 => 24,
                _ => 0,
            };
        }
    }
}
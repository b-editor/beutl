// Image.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;
using BEditor.Drawing.Resources;
using BEditor.Drawing.RowOperation;

using OpenCvSharp;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the image.
    /// </summary>
    /// <typeparam name="T">The type of pixel.</typeparam>
    public unsafe class Image<T> : IDisposable, ICloneable
        where T : unmanaged, IPixel<T>
    {
        // 同じImage<T>型のみで共有される
        private static readonly PixelFormatAttribute _formatAttribute;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _requireDispose = true;
        private T* _pointer;
        private T[]? _array;

        static Image()
        {
            if (Attribute.GetCustomAttribute(typeof(T), typeof(PixelFormatAttribute)) is PixelFormatAttribute attribute)
            {
                _formatAttribute = attribute;
            }
            else
            {
                Debug.Fail(string.Empty);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image{T}"/> class.
        /// </summary>
        /// <param name="width">The width of image.</param>
        /// <param name="height">The height of image.</param>
        public Image(int width, int height)
        {
            ThrowOutOfRange(width, height);

            _width = width;
            _height = height;
            _pointer = (T*)Marshal.AllocCoTaskMem(DataSize);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image{T}"/> class.
        /// </summary>
        /// <param name="width">The width of image.</param>
        /// <param name="height">The height of image.</param>
        /// <param name="data">The image data.</param>
        public Image(int width, int height, T[] data)
        {
            ThrowOutOfRange(width, height);
            if (data is null) throw new ArgumentNullException(nameof(data));

            _requireDispose = false;
            _width = width;
            _height = height;
            _array = data;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image{T}"/> class.
        /// </summary>
        /// <param name="width">The width of image.</param>
        /// <param name="height">The height of image.</param>
        /// <param name="data">The image data.</param>
        public Image(int width, int height, T* data)
        {
            ThrowOutOfRange(width, height);
            if (data is null) throw new ArgumentNullException(nameof(data));

            _requireDispose = false;
            _width = width;
            _height = height;
            _pointer = data;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image{T}"/> class.
        /// </summary>
        /// <param name="width">The width of image.</param>
        /// <param name="height">The height of image.</param>
        /// <param name="data">The image data.</param>
        public Image(int width, int height, IntPtr data)
        {
            ThrowOutOfRange(width, height);
            if (data == IntPtr.Zero) throw new ArgumentNullException(nameof(data));

            _requireDispose = false;
            _width = width;
            _height = height;
            _pointer = (T*)data;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image{T}"/> class.
        /// </summary>
        /// <param name="width">The width of image.</param>
        /// <param name="height">The height of image.</param>
        /// <param name="fill">The color to fill this image with.</param>
        public Image(int width, int height, T fill)
            : this(width, height)
        {
            Fill(fill);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Image{T}"/> class.
        /// </summary>
        ~Image()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the width of the <see cref="Image{T}"/>.
        /// </summary>
        public int Width
        {
            get
            {
                ThrowIfDisposed();
                return _width;
            }
        }

        /// <summary>
        /// Gets the height of the <see cref="Image{T}"/>.
        /// </summary>
        public int Height
        {
            get
            {
                ThrowIfDisposed();
                return _height;
            }
        }

        /// <summary>
        /// Gets the data size of the <see cref="Image{T}"/>.
        /// </summary>
        public int DataSize
        {
            get
            {
                ThrowIfDisposed();
                return Width * Height * sizeof(T);
            }
        }

        /// <summary>
        /// Gets the data of the <see cref="Image{T}"/>.
        /// </summary>
        public Span<T> Data
        {
            get
            {
                ThrowIfDisposed();

                return (_array is null) ? new Span<T>(_pointer, _width * _height) : new Span<T>(_array);
            }
        }

        /// <summary>
        /// Gets the size of the <see cref="Image{T}"/>.
        /// </summary>
        public Size Size
        {
            get
            {
                ThrowIfDisposed();
                return new(Width, Height);
            }
        }

        /// <summary>
        /// Gets the stride width of the <see cref="Image{T}"/>.
        /// </summary>
        public int Stride
        {
            get
            {
                ThrowIfDisposed();
                return Width * sizeof(T);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets or sets the pixel of the <see cref="Image{T}"/>.
        /// </summary>
        /// <param name="x">The horizontal index of the pixel to get.</param>
        /// <param name="y">The vertical index of the pixel to get.</param>
        public ref T this[int x, int y]
        {
            get
            {
                ThrowColOutOfRange(x);

                return ref this[y][x];
            }
        }

        /// <summary>
        /// Gets or sets this <see cref="Image{T}"/> row.
        /// </summary>
        /// <param name="y">The index of the row of the image to get.</param>
        public Span<T> this[int y]
        {
            get
            {
                ThrowIfDisposed();
                ThrowRowOutOfRange(y);

                return Data.Slice(y * Width, Width);
            }

            set
            {
                ThrowIfDisposed();
                ThrowRowOutOfRange(y);

                value.CopyTo(Data.Slice(y * Width, Width));
            }
        }

        /// <summary>
        /// Crops or replaces an image with a range.
        /// </summary>
        /// <param name="roi">The range of images to crop or replace.</param>
        public Image<T> this[Rectangle roi]
        {
            get
            {
                ThrowIfDisposed();
                ThrowOutOfRange(roi);
                var value = new Image<T>(roi.Width, roi.Height);

                Parallel.For(0, roi.Height, new CropOperation<T>(this, value, roi).Invoke);

                return value;
            }

            set
            {
                ThrowIfDisposed();
                ThrowOutOfRange(roi);

                Parallel.For(0, roi.Height, new ReplaceOperation<T>(value, this, roi).Invoke);
            }
        }

        /// <inheritdoc cref="ICloneable.Clone"/>
        public Image<T> Clone()
        {
            ThrowIfDisposed();

            var img = new Image<T>(Width, Height);
            Data.CopyTo(img.Data);

            return img;
        }

        /// <summary>
        /// Clears the pixels of the <see cref="Image{T}"/>.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();

            Data.Clear();
        }

        /// <summary>
        /// Fills the pixels with the specified color.
        /// </summary>
        /// <param name="fill">The color to fill this image with.</param>
        public void Fill(T fill)
        {
            ThrowIfDisposed();
            Data.Fill(fill);
        }

        /// <summary>
        /// Alpha-blend this <see cref="Image{T}"/> with another image.
        /// </summary>
        /// <param name="image">Another image to blend with this image.</param>
        /// <param name="dst">The destination image.</param>
        public void Blend(Image<T> image, Image<T> dst)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            image.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (image.Height != Height) throw new ArgumentOutOfRangeException(nameof(image));
            if (image.Width != Width) throw new ArgumentOutOfRangeException(nameof(image));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* src1Ptr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* src2Ptr = image.Data)
            {
                var proc = new AlphaBlendOperation<T>(src1Ptr, src2Ptr, dstPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

        /// <summary>
        /// Blend this <see cref="Image{T}"/> with another image.
        /// </summary>
        /// <param name="image">Another image to blend with this image.</param>
        /// <param name="dst">The destination image.</param>
        public void Add(Image<T> image, Image<T> dst)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            image.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (image.Height != Height) throw new ArgumentOutOfRangeException(nameof(image));
            if (image.Width != Width) throw new ArgumentOutOfRangeException(nameof(image));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* src1Ptr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* src2Ptr = image.Data)
            {
                var proc = new AddOperation<T>(src1Ptr, src2Ptr, dstPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

        /// <summary>
        /// Blend this <see cref="Image{T}"/> with another image.
        /// </summary>
        /// <param name="image">Another image to blend with this image.</param>
        /// <param name="dst">The destination image.</param>
        public void Subtract(Image<T> image, Image<T> dst)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            image.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (image.Height != Height) throw new ArgumentOutOfRangeException(nameof(image));
            if (image.Width != Width) throw new ArgumentOutOfRangeException(nameof(image));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* src1Ptr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* src2Ptr = image.Data)
            {
                var proc = new SubtractOperation<T>(src1Ptr, src2Ptr, dstPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

        /// <summary>
        /// Flips the <see cref="Image{T}"/>.
        /// </summary>
        /// <param name="mode">The flip mode.</param>
        [SkipLocalsInit]
        public void Flip(FlipMode mode)
        {
            ThrowIfDisposed();
            if (mode.HasFlag(FlipMode.Y))
            {
                Parallel.For(0, Height, y => this[y].Reverse());
            }

            if (mode.HasFlag(FlipMode.X))
            {
                Parallel.For(0, Height / 2, top =>
                {
                    var tmp = (Span<T>)stackalloc T[Width];
                    var bottom = Height - top - 1;

                    var topSpan = this[bottom];
                    var bottomSpan = this[top];

                    topSpan.CopyTo(tmp);
                    bottomSpan.CopyTo(topSpan);
                    tmp.CopyTo(bottomSpan);
                });
            }
        }

        /// <summary>
        /// Makes a border around the <see cref="Image{T}"/>.
        /// </summary>
        /// <param name="top">The number of pixels to insert on top of the original image.</param>
        /// <param name="bottom">The number of pixels to insert on bottom of the original image.</param>
        /// <param name="left">The number of pixels to insert on left of the original image.</param>
        /// <param name="right">The number of pixels to insert on right of the original image.</param>
        /// <returns>Returns an image with a border made from the original image.</returns>
        public Image<T> MakeBorder(int top, int bottom, int left, int right)
        {
            ThrowIfDisposed();

            var width = left + right + Width;
            var height = top + bottom + Height;
            var img = new Image<T>(width, height, default(T));

            img[new Rectangle(left, top, Width, Height)] = this;

            return img;
        }

        /// <summary>
        /// Makes a border around the <see cref="Image{T}"/> by specifying the size.
        /// </summary>
        /// <param name="width">The width of the new <see cref="Image{T}"/>.</param>
        /// <param name="height">The height of the new <see cref="Image{T}"/>.</param>
        /// <returns>Returns an image with a border made from the original image.</returns>
        public Image<T> MakeBorder(int width, int height)
        {
            ThrowIfDisposed();

            var v = (height - Height) / 2;
            var h = (width - Width) / 2;

            return MakeBorder(v, v, h, h);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed && _requireDispose)
            {
                if (_pointer != null) Marshal.FreeCoTaskMem((IntPtr)_pointer);

                _pointer = null;
                _array = null;
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Converts the <see cref="Image{T}"/>.
        /// </summary>
        /// <typeparam name="T2">The type of pixel after conversion.</typeparam>
        /// <returns>Returns the converted image.</returns>
        public Image<T2> Convert<T2>()
            where T2 : unmanaged, IPixel<T2>, IPixelConvertable<T>
        {
            ThrowIfDisposed();
            var dst = new Image<T2>(Width, Height);

            fixed (T* srcPtr = Data)
            fixed (T2* dstPtr = dst.Data)
            {
                Parallel.For(0, Data.Length, new ConvertFromOperation<T, T2>(srcPtr, dstPtr).Invoke);
            }

            return dst;
        }

        /// <summary>
        /// If this object is disposed, then ObjectDisposedException is thrown.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(Image<T>));
        }

        /// <inheritdoc/>
        object ICloneable.Clone() => Clone();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowOutOfRange(int width, int height)
        {
            if (width is < 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Strings.LessThan, nameof(width), 0));
            if (height is < 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Strings.LessThan, nameof(width), 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowColOutOfRange(int x)
        {
            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), string.Format(Strings.LessThan, nameof(x), 0));
            }
            else if (x > Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x), string.Format(Strings.MoreThan, nameof(x), Width));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowRowOutOfRange(int y)
        {
            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y), string.Format(Strings.LessThan, nameof(y), 0));
            }
            else if (y > Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y), string.Format(Strings.MoreThan, nameof(y), Height));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowOutOfRange(Rectangle roi)
        {
            if (roi.Bottom > Height) throw new ArgumentOutOfRangeException(nameof(roi));
            else if (roi.Right > Width) throw new ArgumentOutOfRangeException(nameof(roi));
        }
    }
}
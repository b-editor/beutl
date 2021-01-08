using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.Process;
using BEditor.Drawing.Properties;

using SkiaSharp;

namespace BEditor.Drawing
{
    public unsafe class Image<T> : IDisposable, ICloneable where T : unmanaged, IPixel<T>
    {
        // 同じImage<T>型のみで共有される
        private static readonly PixelFormatAttribute formatAttribute;
        private readonly int height;
        private readonly int width;
        private readonly bool usedispose = true;
        private T* pointer;
        private T[]? array;

        #region Constructors
        static Image()
        {
            if (Attribute.GetCustomAttribute(typeof(T), typeof(PixelFormatAttribute)) is PixelFormatAttribute attribute)
            {
                formatAttribute = attribute;
            }
            else
            {
                Debug.Assert(false);
            }
        }
        /// <summary>
        /// <see cref="Image{T}"/> Initialize a new instance of the class.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is less than 0.</exception>
        public Image(int width, int height)
        {
            ThrowOutOfRange(width, height);

            this.width = width;
            this.height = height;

            pointer = (T*)Marshal.AllocHGlobal(DataSize);
        }
        /// <summary>
        /// <see cref="Image{T}"/> Initialize a new instance of the class.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is less than 0.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        public Image(int width, int height, T[] data)
        {
            ThrowOutOfRange(width, height);
            if (data is null) throw new ArgumentNullException(nameof(data));

            usedispose = false;
            this.width = width;
            this.height = height;
            array = data;
        }
        /// <summary>
        /// <see cref="Image{T}"/> Initialize a new instance of the class.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is less than 0.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        public Image(int width, int height, T* data)
        {
            ThrowOutOfRange(width, height);
            if (data == null) throw new ArgumentNullException(nameof(data));

            usedispose = false;
            this.width = width;
            this.height = height;
            pointer = data;
        }
        /// <summary>
        /// <see cref="Image{T}"/> Initialize a new instance of the class.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is less than 0.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        public Image(int width, int height, IntPtr data)
        {
            ThrowOutOfRange(width, height);
            if (data == IntPtr.Zero) throw new ArgumentNullException(nameof(data));

            usedispose = false;
            this.width = width;
            this.height = height;
            pointer = (T*)data;
        }
        /// <summary>
        /// <see cref="Image{T}"/> Initialize a new instance of the class.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is less than 0.</exception>
        public Image(int width, int height, T fill) : this(width, height)
        {
            Fill(fill);
        }
        ~Image()
        {
            Dispose();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Get the width of this <see cref="Image{T}"/>.
        /// </summary>
        public int Width
        {
            get
            {
                ThrowIfDisposed();
                return width;
            }
        }
        /// <summary>
        /// Get the height of this <see cref="Image{T}"/>.
        /// </summary>
        public int Height
        {
            get
            {
                ThrowIfDisposed();
                return height;
            }
        }
        /// <summary>
        /// Get the data size of this <see cref="Image{T}"/>.
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
        /// Get the data of this <see cref="Image{T}"/>.
        /// </summary>
        public Span<T> Data
        {
            get
            {
                ThrowIfDisposed();

                return (array is null) ? new Span<T>(pointer, width * height) : new Span<T>(array);
            }
        }
        /// <summary>
        /// Get the size of this <see cref="Image{T}"/>.
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
        /// Get the stride width of this <see cref="Image{T}"/>.
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
        /// Get whether an object has been disposed
        /// </summary>
        public bool IsDisposed { get; private set; }

        #endregion

        /// <summary>
        /// Get or set the pixel of this <see cref="Image{T}"/>.
        /// </summary>
        public ref T this[int x, int y]
        {
            get
            {
                ThrowColOutOfRange(x);

                return ref this[y][x];
            }
        }

        /// <summary>
        /// Get or set this <see cref="Image{T}"/> row.
        /// </summary>
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
        /// Crop or replace an image with a range.
        /// </summary>
        public Image<T> this[Rectangle roi]
        {
            set
            {
                ThrowIfDisposed();
                ThrowOutOfRange(roi);

                Parallel.For(0, roi.Height, y =>
                {
                    var sourceRow = value[y];
                    var targetRow = this[y + roi.Y].Slice(roi.X, roi.Width);

                    sourceRow.CopyTo(targetRow);
                });
            }
            get
            {
                ThrowIfDisposed();
                ThrowOutOfRange(roi);
                var value = new Image<T>(roi.Width, roi.Height);

                Parallel.For(0, roi.Height, y =>
                {
                    var sourceRow = this[y + roi.Y].Slice(roi.X, roi.Width);
                    var targetRow = value[y];

                    sourceRow.Slice(0, roi.Width).CopyTo(targetRow);
                });

                return value;
            }
        }

        #region Methods

        public Image<T> Clone()
        {
            ThrowIfDisposed();

            var img = new Image<T>(Width, Height);
            Data.CopyTo(img.Data);

            return img;
        }

        public void Clear()
        {
            ThrowIfDisposed();

            Data.Clear();
        }

        public void Fill(T fill)
        {
            ThrowIfDisposed();
            Data.Fill(fill);
        }

        [SkipLocalsInit]
        public void Flip(FlipMode mode)
        {
            ThrowIfDisposed();
            if (mode.HasFlag(FlipMode.Y))
            {
                Parallel.For(0, Height, y =>
                {
                    this[y].Reverse();
                });
            }
            
            if (mode.HasFlag(FlipMode.X))
            {
                Parallel.For(0, Height / 2, top =>
                {
                    Span<T> tmp = stackalloc T[Width];
                    var bottom = Height - top - 1;

                    var topSpan = this[bottom];
                    var bottomSpan = this[top];

                    topSpan.CopyTo(tmp);
                    bottomSpan.CopyTo(topSpan);
                    tmp.CopyTo(bottomSpan);
                });
            }
        }
        public Image<T> MakeBorder(int top, int bottom, int left, int right)
        {
            ThrowIfDisposed();

            var width = left + right + Width;
            var height = top + bottom + Height;
            var img = new Image<T>(width, height, default(T));

            img[new Rectangle(left, top, Width, Height)] = this;

            return img;
        }
        public Image<T> MakeBorder(int width, int height)
        {
            ThrowIfDisposed();

            int v = (height - Height) / 2;
            int h = (width - Width) / 2;

            return MakeBorder(v, v, h, h);
        }

        public void Dispose()
        {
            if (!IsDisposed && usedispose)
            {
                if (pointer != null) Marshal.FreeHGlobal((IntPtr)pointer);

                pointer = null;
                array = null;

                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        object ICloneable.Clone() => this.Clone();

        public Image<T2> Convert<T2>() where T2 : unmanaged, IPixel<T2>, IPixelConvertable<T>
        {
            ThrowIfDisposed();
            var dst = new Image<T2>(Width, Height);

            fixed (T* srcPtr = Data)
            fixed (T2* dstPtr = dst.Data)
            {
                Parallel.For(0, Data.Length, new ConvertFromProcess<T, T2>(srcPtr, dstPtr).Invoke);
            }

            return dst;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowOutOfRange(int width, int height)
        {
            if (width is <= 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Resources.LessThan, nameof(width), 0));
            if (height is <= 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Resources.LessThan, nameof(width), 0));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowColOutOfRange(int x)
        {
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), string.Format(Resources.LessThan, nameof(x), 0));

            else if (x > Height) throw new ArgumentOutOfRangeException(nameof(x), string.Format(Resources.MoreThan, nameof(x), Width));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowRowOutOfRange(int y)
        {
            if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), string.Format(Resources.LessThan, nameof(y), 0));

            else if (y > Height) throw new ArgumentOutOfRangeException(nameof(y), string.Format(Resources.MoreThan, nameof(y), Height));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowOutOfRange(Rectangle roi)
        {
            if (roi.Bottom > Height) throw new ArgumentOutOfRangeException(nameof(roi));
            else if (roi.Right > Width) throw new ArgumentOutOfRangeException(nameof(roi));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(Image<T>));
        }

        #endregion
    }
}

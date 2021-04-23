using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;
using BEditor.Drawing.Resources;
using BEditor.Drawing.RowOperation;

namespace BEditor.Drawing
{
    public unsafe class Image<T> : IDisposable, ICloneable, IAsyncDisposable where T : unmanaged, IPixel<T>
    {
        // 同じImage<T>型のみで共有される
        private static readonly PixelFormatAttribute formatAttribute;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _usedispose = true;
        private T* _pointer;
        private T[]? _array;

        #region Constructors
        static Image()
        {
            if (Attribute.GetCustomAttribute(typeof(T), typeof(PixelFormatAttribute)) is PixelFormatAttribute attribute)
            {
                formatAttribute = attribute;
            }
            else
            {
                Debug.Fail(string.Empty);
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

            this._width = width;
            this._height = height;

            _pointer = (T*)Marshal.AllocHGlobal(DataSize);
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

            _usedispose = false;
            this._width = width;
            this._height = height;
            _array = data;
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

            _usedispose = false;
            this._width = width;
            this._height = height;
            _pointer = data;
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

            _usedispose = false;
            this._width = width;
            this._height = height;
            _pointer = (T*)data;
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
                return _width;
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
                return _height;
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

                return (_array is null) ? new Span<T>(_pointer, _width * _height) : new Span<T>(_array);
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

                Parallel.For(0, roi.Height, new ReplaceOperation<T>(value, this, roi).Invoke);
            }
            get
            {
                ThrowIfDisposed();
                ThrowOutOfRange(roi);
                var value = new Image<T>(roi.Width, roi.Height);

                Parallel.For(0, roi.Height, new CropOperation<T>(this, value, roi).Invoke);

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

        public void Blend(Image<T> mask, Image<T> dst)
        {
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            mask.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (mask.Height != Height) throw new ArgumentOutOfRangeException(nameof(mask));
            if (mask.Width != Width) throw new ArgumentOutOfRangeException(nameof(mask));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* srcPtr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* maskPtr = mask.Data)
            {
                var proc = new AlphaBlendOperation<T>(srcPtr, dstPtr, maskPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

        public void Add(Image<T> mask, Image<T> dst)
        {
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            mask.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (mask.Height != Height) throw new ArgumentOutOfRangeException(nameof(mask));
            if (mask.Width != Width) throw new ArgumentOutOfRangeException(nameof(mask));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* srcPtr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* maskPtr = mask.Data)
            {
                var proc = new AddOperation<T>(srcPtr, dstPtr, maskPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

        public void Subtract(Image<T> mask, Image<T> dst)
        {
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            if (dst is null) throw new ArgumentNullException(nameof(dst));
            mask.ThrowIfDisposed();
            dst.ThrowIfDisposed();
            if (mask.Height != Height) throw new ArgumentOutOfRangeException(nameof(mask));
            if (mask.Width != Width) throw new ArgumentOutOfRangeException(nameof(mask));
            if (dst.Height != Height) throw new ArgumentOutOfRangeException(nameof(dst));
            if (dst.Width != Width) throw new ArgumentOutOfRangeException(nameof(dst));

            fixed (T* srcPtr = Data)
            fixed (T* dstPtr = dst.Data)
            fixed (T* maskPtr = mask.Data)
            {
                var proc = new SubtractOperation<T>(srcPtr, dstPtr, maskPtr);
                Parallel.For(0, Data.Length, proc.Invoke);
            }
        }

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

            var v = (height - Height) / 2;
            var h = (width - Width) / 2;

            return MakeBorder(v, v, h, h);
        }

        public void Dispose()
        {
            if (!IsDisposed && _usedispose)
            {
                if (_pointer != null) Marshal.FreeHGlobal((IntPtr)_pointer);

                _pointer = null;
                _array = null;
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            if (IsDisposed && !_usedispose) return default;

            var task = Task.Run(() =>
            {
                if (_pointer != null) Marshal.FreeHGlobal((IntPtr)_pointer);

                _pointer = null;
                _array = null;
            });

            IsDisposed = true;

            GC.SuppressFinalize(this);
            return new(task);
        }

        public Image<T2> Convert<T2>() where T2 : unmanaged, IPixel<T2>, IPixelConvertable<T>
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(Image<T>));
        }

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

        #endregion
    }
}
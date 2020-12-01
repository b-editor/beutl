using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Interop;

namespace BEditor.Drawing
{
    public unsafe class Image<T> : ICloneable where T : unmanaged, IPixel<T>
    {
        // 同じImage<T>型のみで共有される
        private static readonly T s = new();

        #region Constructors
        public Image(int width, int height)
        {
            Width = width;
            Height = height;
            // Todo: ArrayPool
            Data = new T[width * height];
        }
        public Image(int width, int height, T[] data) : this(width, height)
        {
            fixed (T* src = &data[0], dst = &Data[0])
            {
                var size = DataSize;
                Buffer.MemoryCopy(src, dst, size, size);
            }
        }
        public Image(int width, int height, T* data) : this(width, height)
        {
            fixed (T* dst = &Data[0])
            {
                var size = DataSize;
                Buffer.MemoryCopy(data, dst, size, size);
            }
        }
        public Image(int width, int height, IntPtr data) : this(width, height, (T*)data)
        {

        }

        #endregion

        #region Properties

        public int Width { get; }
        public int Height { get; }
        public int DataSize => Width * Height * sizeof(T);
        // Data は ArrayPool からの可能性があるのでサイズから求める
        public int Length => Width * Height;
        public T[] Data { get; }
        public Size Size => new(Width, Height);
        public int Stride => Width * sizeof(T);

        #region Strideの説明
        /*
         width: 3
         height: 2
         
         　ここがStride
         |------------|
         BGRA BGRA BGRA
         BGRA BGRA BGRA

         byte* のときに使用
         */
        #endregion


        #endregion

        public T this[int x, int y]
        {
            set
            {
                GetRowSpan(y)[x] = value;
            }
            get
            {
                return GetRowSpan(y)[x];
            }
        }
        public Span<T> this[int y]
        {
            get => GetRowSpan(y);
            set => SetRowSpan(y, value);
        }

        public Image<T> this[Rectangle roi]
        {
            set
            {
                var rowop = new RowOperation(roi, value, this);

                Parallel.For(0, roi.Y, rowop.Invoke);
            }
            get
            {
                var value = new Image<T>(roi.Width, roi.Height);
                var rowop = new RowOperation(roi, this, value);
                Parallel.For(0, roi.Y, rowop.Invoke);

                return value;
            }
        }

        #region Methods

        public static Image<T> FromStream(Stream stream, ImageReadMode mode = ImageReadMode.Color)
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var array = memoryStream.ToArray();
            Native.Image_Decode(array, new IntPtr(array.Length), (int)mode, out var image);

            var ret = new Image<T>(image.Width, image.Height, (T*)image.Data);
            Marshal.FreeHGlobal((IntPtr)image.Data);

            return ret;
        }

        public object Clone() =>
            new Image<T>(Width, Height, Data);
        public void Clear() =>
            Array.Clear(Data, 0, Width * Height);

        public Span<T> GetRowSpan(int y)
        {
            var span = new Span<T>(Data);
            return span.Slice(y * Width, Width);
        }
        public void SetRowSpan(int y, Span<T> src)
        {
            var span = new Span<T>(Data);
            src.CopyTo(span.Slice(y * Width, Width));
        }

        public bool Save(string filename)
        {
            fixed (T* data = &Data[0])
            {
                return Native.Image_Save(new()
                {
                    Width = Width,
                    Height = Height,
                    CvType = s.CvType,
                    Data = data
                },
                filename);
            }
        }

        public void Flip(FlipMode mode)
        {
            fixed (T* data = Data)
            {
                Native.Image_Flip(ToStruct(data), (int)mode);
            }
        }
        public void AreaExpansion(int top, int bottom, int left, int right)
        {
            fixed (T* data = Data)
            {
                Native.Image_AreaExpansion(ToStruct(data), top, bottom, left, right);
            }
        }


        internal ImageStruct ToStruct(T* data) => new()
        {
            Width = Width,
            Height = Height,
            CvType = s.CvType,
            Data = data
        };

        #endregion


        private readonly struct RowOperation
        {
            private readonly Rectangle bounds;
            private readonly Image<T> src;
            private readonly Image<T> dst;

            public RowOperation(Rectangle bounds, Image<T> src, Image<T> dst)
            {
                this.bounds = bounds;
                this.src = src;
                this.dst = dst;
            }

            public void Invoke(int y)
            {
                Span<T> sourceRow = src.GetRowSpan(y).Slice(bounds.Left);
                Span<T> targetRow = dst.GetRowSpan(y - bounds.Top);
                sourceRow.Slice(0, bounds.Width).CopyTo(targetRow);
            }
        }
    }
}

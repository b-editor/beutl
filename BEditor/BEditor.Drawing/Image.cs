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
    public unsafe class Image<T> : ICloneable, IDisposable where T : unmanaged, IPixel<T>
    {
        // 同じImage<T>型のみで共有される
        private static readonly T s = new();

        #region Constructors
        public Image(int width, int height)
        {
            Width = width;
            Height = height;
            // Todo: ArrayPool
            Data = new T[width * height];//ArrayPool<T>.Shared.Rent(width * height);
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
        public Image(int width, int height, T fill) : this(width, height)
        {
            Fill(fill);
        }

        #endregion

        #region Properties

        public int Width { get; }
        public int Height { get; }
        public int DataSize => Width * Height * sizeof(T);
        // Data は ArrayPool からの可能性があるのでサイズから求める
        public int Length => Width * Height;
        public T[] Data { get; private set; }
        public Size Size => new(Width, Height);
        public int Stride => Width * sizeof(T);
        public bool IsDisposed { get; private set; }

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
                Parallel.For(roi.Y, roi.Height, y =>
                 {
                     var sourceRow = value.GetRowSpan(y - roi.Y);
                     var targetRow = this.GetRowSpan(y).Slice(roi.X, roi.Width);

                     sourceRow.CopyTo(targetRow);
                 });
            }
            get
            {
                var value = new Image<T>(roi.Width, roi.Height);

                Parallel.For(roi.Y, roi.Height, y =>
                {
                    var sourceRow = this.GetRowSpan(y).Slice(roi.X, roi.Width);
                    var targetRow = value.GetRowSpan(y - roi.Y);

                    sourceRow.Slice(0, roi.Width).CopyTo(targetRow);
                });

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
        public void Fill(T fill)
        {
            Parallel.For(0, Height, y =>
            {
                GetRowSpan(y).Fill(fill);
            });
        }

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
            if (mode is FlipMode.Y)
            {
                Parallel.For(0, Height, y =>
                {
                    GetRowSpan(y).Reverse();
                });
            }
            else
            {
                Parallel.For(0, Height / 2, top =>
                {
                    Span<T> tmp = stackalloc T[Width];
                    var bottom = Height - top - 1;

                    var topSpan = GetRowSpan(bottom);
                    var bottomSpan = GetRowSpan(top);

                    topSpan.CopyTo(tmp);
                    bottomSpan.CopyTo(topSpan);
                    tmp.CopyTo(bottomSpan);
                });
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
        public void Dispose()
        {
            //if (!IsDisposed)
            //{
            //    ArrayPool<T>.Shared.Return(Data, true);
            //    Data = null;
            //}
        }

        #endregion

    }
}

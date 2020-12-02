using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Drawing;
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
            Data = ArrayPool<T>.Shared.Rent(width * height);
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
        private Image(ImageStruct image)
        {
            Width = image.Width;
            Height = image.Height;

            Data = ArrayPool<T>.Shared.Rent(Width * Height);

            fixed (T* dst = &Data[0])
            {
                var size = DataSize;
                Buffer.MemoryCopy(image.Data, dst, size, size);
            }
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
            set => this[y][x] = value;
            get => this[y][x];
        }
        public Span<T> this[int y]
        {
            get => new Span<T>(Data).Slice(y * Width, Width);
            set => value.CopyTo(new Span<T>(Data).Slice(y * Width, Width));
        }

        public Image<T> this[Rectangle roi]
        {
            set
            {
                Parallel.For(0, roi.Height, y =>
                {
                    var sourceRow = value[y];
                    var targetRow = this[y + roi.Y].Slice(roi.X, roi.Width);

                    sourceRow.CopyTo(targetRow);
                });
            }
            get
            {
                var value = new Image<T>(roi.Width, roi.Height);

                Parallel.For(roi.Y, roi.Height, y =>
                {
                    var sourceRow = this[y].Slice(roi.X, roi.Width);
                    var targetRow = value[y - roi.Y];

                    sourceRow.Slice(0, roi.Width).CopyTo(targetRow);
                });

                return value;
            }
        }

        #region Methods

        public object Clone() =>
            new Image<T>(Width, Height, Data);
        public void Clear() =>
            Array.Clear(Data, 0, Width * Height);
        public void Fill(T fill)
        {
            Parallel.For(0, Height, y =>
            {
                this[y].Fill(fill);
            });
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
                    this[y].Reverse();
                });
            }
            else
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
            var width = left + right + Width;
            var height = top + bottom + Height;
            var img = new Image<T>(width, height);

            img[new Rectangle(left, top, Width, Height)] = this;

            return img;
        }
        public Image<T> BoxBlur(float size)
        {
            var img = new Image<T>(Width, Height);
            fixed (T* data = Data)
            fixed (T* outdata = img.Data)
            {
                Native.Image_BoxBlur(ToStruct(data), size, img.ToStruct(outdata));

                return img;
            }
        }
        public Image<T> GanssBlur(float size)
        {
            var img = new Image<T>(Width, Height);
            fixed (T* data = Data)
            fixed (T* outdata = img.Data)
            {
                Native.Image_GaussBlur(ToStruct(data), size, img.ToStruct(outdata));

                return img;
            }
        }
        public Image<T> MedianBlur(int size)
        {
            var img = new Image<T>(Width, Height);
            fixed (T* data = Data)
            fixed (T* outdata = img.Data)
            {
                Native.Image_MedianBlur(ToStruct(data), size, img.ToStruct(outdata));

                return img;
            }
        }
        public Image<T> Dilate(int f)
        {
            var img = new Image<T>(Width, Height);
            fixed (T* data = Data)
            fixed (T* outdata = img.Data)
            {
                Native.Image_Dilate(ToStruct(data), f, img.ToStruct(outdata));

                return img;
            }
        }
        public Image<T> Erode(int f)
        {
            var img = new Image<T>(Width, Height);
            fixed (T* data = Data)
            fixed (T* outdata = img.Data)
            {
                Native.Image_Erode(ToStruct(data), f, img.ToStruct(outdata));

                return img;
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
            if (!IsDisposed)
            {
                ArrayPool<T>.Shared.Return(Data, true);
                Data = null;
            }
        }

        #endregion

#if DEBUG
        ~Image()
        {
            Debug.WriteLine($"Delete: Image<{typeof(T).Name}> Width:{Width} Height:{Height}");
        }
#endif
    }

    public static partial class Image
    {
        public static Image<BGRA32> FromStream(Stream stream)
        {
            using var bmp = new Bitmap(stream);
            var r = bmp.ToImage();

            return r;
        }
        public static Image<BGRA32> FromFile(string filename)
        {
            using var bmp = new Bitmap(filename);
            var r = bmp.ToImage();

            return r;
        }
    }
}

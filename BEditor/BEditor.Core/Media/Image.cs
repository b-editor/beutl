#define UseOpenGL

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Exceptions;
using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Native;
using BEditor.Core.Renderings;
using BEditor.Core.Service;

namespace BEditor.Core.Media
{
    /// <summary>
    /// OpenCv Mat を利用した画像オブジェクトを表します
    /// </summary>
    public unsafe class Image : DisposableObject
    {
        #region Constructor

        /// <summary>
        /// <see cref="Image"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">画像の横幅</param>
        /// <param name="height">画像の高さ</param>
        /// <exception cref="NativeException"/>
        public Image(int width, int height)
        {
            var result = ImageProcess.Create(width, height, ImageType.ByteCh4, out ptr);

            if (result != null)
            {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// <see cref="Image"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">画像の横幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="type">画像のチャンネル数などの情報</param>
        /// <exception cref="NativeException"/>
        public Image(int width, int height, ImageType type)
        {
            var result = ImageProcess.Create(width, height, type, out ptr);

            if (result != null)
            {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// 画像データから <see cref="Image"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">画像の横幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="type">画像のチャンネル数などの情報</param>
        /// <param name="data">画像のデータ</param>
        /// <remarks>画像のデータはコピーされません</remarks>
        /// <exception cref="IntPtrZeroException"/>
        /// <exception cref="NativeException"/>
        public Image(int width, int height, ImageType type, IntPtr data)
        {
            if (data == IntPtr.Zero) throw new IntPtrZeroException(nameof(data));

            var result = ImageProcess.Create(width, height, data, type, out this.ptr);

            if (result != null)
            {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// OpenCv Matのポインタから <see cref="Image"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="ptr">OpenCv Matのポインタ</param>
        /// <remarks>画像のデータはコピーされません</remarks>
        /// <exception cref="IntPtrZeroException"/>
        public Image(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) throw new IntPtrZeroException(nameof(ptr));

            this.ptr = ptr;
        }
        //Memo : Xmlここまで
        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <param name="rect"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Image(Image image, Rectangle rect)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            var result = ImageProcess.Create(image.ptr, rect.X, rect.Y, rect.Width, rect.Height, out ptr);

            if (result != null)
            {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <exception cref="Exception"/>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="NativeException"/>
        [Obsolete("未調整のため public Image(Stram, ImageReadMode)を利用してください")] //Todo : Imageのファイル名から初期化を調整
        public Image(string file)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            var result = ImageProcess.Decode(file, out ptr);

            if (result != null)
            {
                throw new NativeException(result);
            }

            if (ptr == IntPtr.Zero)
                throw new Exception();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="mode"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="NativeException"/>
        public Image(Stream stream, ImageReadMode mode = ImageReadMode.Color)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (stream.Length > int.MaxValue)
                throw new ArgumentException("Not supported stream (too long)");

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var array = memoryStream.ToArray();
            var result = ImageProcess.Decode(array, new IntPtr(array.Length), (int)mode, out ptr);

            if (result != null) throw new NativeException(result);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="mode"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="NativeException"/>
        public Image(byte[] imageBytes, ImageReadMode mode = ImageReadMode.Color)
        {
            if (imageBytes == null)
                throw new ArgumentNullException(nameof(imageBytes));

            var result = ImageProcess.Decode(imageBytes, new IntPtr(imageBytes.Length), (int)mode, out ptr);

            if (result != null) throw new NativeException(result);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="Exception"/>
        public Image()
        {
            var result = ImageProcess.Create(out ptr);

            if (result != null)
            {
                throw new Exception(result);
            }
        }

        #endregion


        #region Properties

        private IntPtr ptr;
        /// <summary>
        /// 
        /// </summary>
        public IntPtr Ptr { get => ptr; set => ptr = value; }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public IntPtr Data => new IntPtr(DataPointer);
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public IntPtr DataEnd
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.DataEnd(ptr, out var ret);
                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public byte* DataPointer
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Data(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Width
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Width(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Height
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Height(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public Size Size => new Size(Width, Height);
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public ImageType Type
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Type(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public long Step
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Step(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret.ToInt64();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int ElemSize
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.ElemSize(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret.ToInt32();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public bool IsContinuous
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.IsContinuous(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret != 0;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public bool IsSubmatrix
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.IsSubmatrix(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret != 0;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Depth
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Depth(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Channels
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Channels(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public long Total
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Total(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret.ToInt64();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Dimension
        {
            get
            {
                ThrowIfDisposed();
                var result = ImageProcess.Dimension(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                return ret;
            }
        }

        public Point3 Coord { get; set; }
        public Point3 Center { get; set; }
        public Point3 Rotate { get; set; }
        public Point3 Scale { get; set; } = new(1f, 1f, 1f);
        public MaterialRecord Material { get; set; } = new MaterialRecord(
            new(1f, 1f, 1f, 1f),
            new(1f, 1f, 1f, 1f),
            new(1f, 1f, 1f, 1f),
            16f,
            new(1f, 1f, 1f, 1f));

        #endregion


        #region Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <exception cref="ObjectDisposedException"/>
        public void ForEach(Action<int, int, int, int> action)
        {
            ThrowIfDisposed();
            //int bitcount = Width * Height * Channels * Type.Bits / 8;

            byte* pixelPtr = DataPointer;
            var step = (int)Step;
            var elemsize = ElemSize;


            Parallel.For(0, Height, y =>
            {
                Parallel.For(0, Width, x =>
                {
                    //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                    int pos = y * step + x * elemsize;

                    // BGRA
                    action?.Invoke(pos, pos + 1, pos + 2, pos + 3);
                });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public Image Clone()
        {
            ThrowIfDisposed();
            var result = ImageProcess.Clone(ptr, out var ret);

            if (result != null) throw new NativeException(result);

            var retVal = new Image(ret);
            return retVal;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public Image Clone(Rectangle rect)
        {
            using var part = new Image(this, rect);
            return part.Clone();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ArgumentNullException"/>
        public bool Save(string file)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));
            ThrowIfDisposed();

            var result = ImageProcess.Save(ptr, file, out var ret);

            if (result != null) throw new NativeException(result);

            return ret != 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rowStart"></param>
        /// <param name="rowEnd"></param>
        /// <param name="colStart"></param>
        /// <param name="colEnd"></param>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ArgumentException"/>
        public Image SubMatrix(int rowStart, int rowEnd, int colStart, int colEnd)
        {
            if (colStart >= colEnd)
                throw new ArgumentException("heightStart >= heightEnd");
            if (rowStart >= rowEnd)
                throw new ArgumentException("widthStart >= widthEnd");

            ThrowIfDisposed();
            var result = ImageProcess.SubMatrix(ptr, rowStart, rowEnd, colStart, colEnd, out var ret);

            if (result != null) throw new NativeException(result);

            var retVal = new Image(ret);
            return retVal;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rowRange"></param>
        /// <param name="colRange"></param>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ArgumentException"/>
        public Image SubMatrix(Range rowRange, Range colRange)
        {
            return SubMatrix(rowRange.Start, rowRange.End, colRange.Start, colRange.End);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ArgumentException"/>
        public Image SubMatrix(Rectangle roi)
        {
            return SubMatrix(roi.Y, roi.Y + roi.Height, roi.X, roi.X + roi.Width);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void CopyTo(Image image)
        {
            ThrowIfDisposed();
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            var result = ImageProcess.CopyTo(ptr, out image.ptr);

            if (result != null) throw new NativeException(result);
        }

        #endregion


        #region インデクサ

        /// <summary>
        /// 
        /// </summary>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public Image this[Rectangle roi]
        {
            get => SubMatrix(roi);
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                value.ThrowIfDisposed();
                if (Dimension != value.Dimension)
                    throw new ArgumentException();

                if (roi.Size != value.Size)
                    throw new ArgumentException();
                var sub = SubMatrix(roi);
                value.CopyTo(sub);
            }
        }

        #endregion

        #region StaticInit

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="line"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        /// <exception cref="NativeException"/>
        [return: MaybeNull()]
        public static Image Ellipse(int width, int height, int line, in ReadOnlyColor color)
        {
            return Services.ImageRenderService.Ellipse(width, height, line, color);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="line"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        [return: MaybeNull()]
        public static Image Rectangle(int width, int height, int line, in ReadOnlyColor color)
        {
            return Services.ImageRenderService.Rectangle(width, height, line, color);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size"></param>
        /// <param name="color"></param>
        /// <param name="text"></param>
        /// <param name="font"></param>
        /// <param name="style"></param>
        /// <param name="rightToLeft"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"/>
        [return: MaybeNull()]
        public static Image Text(int size, in ReadOnlyColor color, string text, FontRecord font, string style, bool rightToLeft)
        {
            return Services.ImageRenderService.Text(size, color, text, font, style, rightToLeft);
        }

        #endregion

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Ptr == IntPtr.Zero) return base.ToString();
            return $"(Width:{Width} Height:{Height} Type:{Type} Data:{Data})";
        }

        protected override void OnDispose(bool disposing)
        {
            if (ptr != IntPtr.Zero)
            {
                //ImageProcess.Release(ptr);
                ImageProcess.Delete(ptr);
            }

            ptr = IntPtr.Zero;
        }
        internal static IntPtr UnmanageAlloc()
        {
            var result = ImageProcess.Create(1, 1, ImageType.ByteCh4, out var ptr);

            if (result != null)
            {
                throw new Exception(result);
            }

            return ptr;
        }
    }

    public record MaterialRecord(Color Ambient, Color Diffuse, Color Specular, float Shininess, Color Color);

    public enum FlipMode
    {
        XY = -1,
        X = 0,
        Y = 1
    }
}

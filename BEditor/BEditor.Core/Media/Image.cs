#define UseOpenGL

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using BEditor.Core.Exceptions;
using BEditor.Core.Native;
using BEditor.Core.Renderer;

namespace BEditor.Core.Media {
    /// <summary>
    /// BGRA
    /// </summary>
    public unsafe class Image : DisposableObject, IEquatable<Image> {
        #region Constructor

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <exception cref="NativeException"/>
        public Image(in int width, in int height) {
            var result = ImageProcess.Create(width, height, ImageType.ByteCh4, out ptr);

            if (result != null) {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="type"></param>
        /// <exception cref="NativeException"/>
        public Image(in int width, in int height, in ImageType type) {
            var result = ImageProcess.Create(width, height, type, out ptr);

            if (result != null) {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// コピーしない
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="ptr"></param>
        /// <param name="type"></param>
        /// <exception cref="IntPtrZeroException"/>
        /// <exception cref="NativeException"/>
        public Image(in int width, in int height, in IntPtr ptr, in ImageType type) {
            if (ptr == IntPtr.Zero) throw new IntPtrZeroException(nameof(ptr));

            var result = ImageProcess.Create(width, height, ptr, type, out this.ptr);

            if (result != null) {
                throw new NativeException(result);
            }
        }
        /// <summary>
        /// コピーしない
        /// </summary>
        /// <param name="ptr"></param>
        /// <exception cref="IntPtrZeroException"/>
        public Image(in IntPtr ptr) {
            if (ptr == IntPtr.Zero) throw new IntPtrZeroException(nameof(ptr));

            this.ptr = ptr;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <param name="rect"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="NativeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Image(Image image, Rectangle rect) {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            var result = ImageProcess.Create(image.ptr, rect.X, rect.Y, rect.Width, rect.Height, out ptr);

            if (result != null) {
                throw new NativeException(result);
            }

            GC.KeepAlive(image);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <exception cref="Exception"/>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="NativeException"/>
        public Image(string file) {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            var result = ImageProcess.Decode(file, out ptr);

            if (result != null) {
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
        public Image(Stream stream, in ImageReadMode mode = ImageReadMode.Color) {
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
        public Image(byte[] imageBytes, in ImageReadMode mode = ImageReadMode.Color) {
            if (imageBytes == null)
                throw new ArgumentNullException(nameof(imageBytes));

            var result = ImageProcess.Decode(imageBytes, new IntPtr(imageBytes.Length), (int)mode, out ptr);

            if (result != null) throw new NativeException(result);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="Exception"/>
        public Image() {
            var result = ImageProcess.Create(out ptr);

            if (result != null) {
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
        public IntPtr DataEnd {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.DataEnd(ptr, out var ret);
                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public byte* DataPointer {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Data(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Width {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Width(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Height {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Height(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
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
        public ImageType Type {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Type(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public long Step {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Step(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret.ToInt64();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int ElemSize {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.ElemSize(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret.ToInt32();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public bool IsContinuous {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.IsContinuous(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret != 0;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public bool IsSubmatrix {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.IsSubmatrix(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret != 0;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Depth {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Depth(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Channels {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Channels(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public long Total {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Total(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret.ToInt64();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public int Dimension {
            get {
                ThrowIfDisposed();
                var result = ImageProcess.Dimension(ptr, out var ret);

                if (result != null) throw new NativeException(result);

                GC.KeepAlive(this);
                return ret;
            }
        }

        #endregion


        #region Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <exception cref="ObjectDisposedException"/>
        public void ForEach(Action<int, int, int, int> action) {
            ThrowIfDisposed();
            int bitcount = Width * Height * Channels * Type.Bits / 8;

            byte* pixelPtr = DataPointer;
            var step = (int)Step;
            var elemsize = ElemSize;


            Parallel.For(0, Height, y => {
                Parallel.For(0, Width, x => {
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
        public Image Clone() {
            ThrowIfDisposed();
            var result = ImageProcess.Clone(ptr, out var ret);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
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
        public Image Clone(Rectangle rect) {
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
        public bool Save(string file) {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));
            ThrowIfDisposed();

            var result = ImageProcess.Save(ptr, file, out var ret);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
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
        public Image SubMatrix(in int rowStart, in int rowEnd, in int colStart, in int colEnd) {
            if (colStart >= colEnd)
                throw new ArgumentException("heightStart >= heightEnd");
            if (rowStart >= rowEnd)
                throw new ArgumentException("widthStart >= widthEnd");

            ThrowIfDisposed();
            var result = ImageProcess.SubMatrix(ptr, rowStart, rowEnd, colStart, colEnd, out var ret);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
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
        public Image SubMatrix(Range rowRange, Range colRange) {
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
        public Image SubMatrix(Rectangle roi) {
            return SubMatrix(roi.Y, roi.Y + roi.Height, roi.X, roi.X + roi.Width);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void CopyTo(Image image) {
            ThrowIfDisposed();
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            var result = ImageProcess.CopyTo(ptr, out image.ptr);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
            GC.KeepAlive(image);
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
        public Image this[Rectangle roi] {
            get => SubMatrix(roi);
            set {
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
        public static Image Ellipse(in int width, in int height, in int line, Color color) {
            if (EllipseFunc == null) {
                var result = ImageProcess.Ellipse(width, height, line, color.R, color.G, color.B, out var ptr);

                if (result != null) throw new NativeException(result);

                return new Image(ptr);
            }
            else {
                return EllipseFunc(width, height, line, color);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="line"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        public static Image Rectangle(in int width, in int height, in int line, Color color) {
            return RectangleFunc?.Invoke(width, height, line, color);
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
        public static Image Text(in int size, Color color, string text, Font font, string style, in bool rightToLeft) {
            if (string.IsNullOrEmpty(text)) return null;

            //intへ変換
            var styleint = style switch
            {
                "Normal" => SDL2.TTF.FontStyle.Normal,
                "Bold" => SDL2.TTF.FontStyle.Bold,
                "Italic" => SDL2.TTF.FontStyle.Italic,
                "UnderLine" => SDL2.TTF.FontStyle.UnderLine,
                "StrikeThrough" => SDL2.TTF.FontStyle.StrikeThrough,
                _ => throw new NotImplementedException(),
            };
            var fontp = new SDL2.TTF.Font(font.Path, size) { Style = styleint };

            var result = fontp.RenderUTF8(text, color);
            fontp.Dispose();

            return result;
        }

        public static Func<int, int, int, Color, Image> EllipseFunc { get; set; }
        public static Func<int, int, int, Color, Image> RectangleFunc { get; set; }

        #endregion


        #region Flip

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mode"></param>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void Flip(in FlipMode mode) {
            ThrowIfDisposed();

            var result = ImageProcess.Flip(ptr, (int)mode);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        #endregion

        #region AreaExpansion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="top"></param>
        /// <param name="bottom"></param>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void AreaExpansion(in int top, in int bottom, in int left, in int right) {
            ThrowIfDisposed();
            var result = ImageProcess.AreaExpansion(ptr, top, bottom, left, right);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void AreaExpansion(in int width, in int height) {
            ThrowIfDisposed();

            int v = (height - Height) / 2;
            int h = (width - Width) / 2;

            AreaExpansion(v, v, h, h);

            GC.KeepAlive(this);
        }

        #endregion

        #region Blurs

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blurSize"></param>
        /// <param name="alphaBlur"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void Blur(in int blurSize, in bool alphaBlur) {
            ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blursize < 0");
            if (blurSize == 0) return;

            var result = ImageProcess.Blur(ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blurSize"></param>
        /// <param name="alphaBlur"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void GaussianBlur(in int blurSize, in bool alphaBlur) {
            ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0");
            if (blurSize == 0) return;

            var result = ImageProcess.GaussianBlur(ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blurSize"></param>
        /// <param name="alphaBlur"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void MedianBlur(in int blurSize, in bool alphaBlur) {
            ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0", nameof(blurSize));
            if (blurSize == 0) return;

            var result = ImageProcess.MedianBlur(ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        #endregion

        #region Border

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size"></param>
        /// <param name="color"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void Border(in int size, Color color) {
            if (size <= 0) throw new ArgumentException("size <= 0");
            ThrowIfDisposed();

            int nwidth = Width + (size + 5) * 2;
            int nheight = Height + (size + 5) * 2;

#if UseOpenGL
            ImageHelper.renderer.Clear(nwidth, nheight);


            //縁取りを描画
            var mask = Clone();
            mask.SetColor(color);
            mask.AreaExpansion(nwidth, nheight);
            mask.Dilate(size);

            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(mask));

            mask.Dispose();
            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(this));


            ImageProcess.Delete(Ptr);
            Disposable.Dispose();

            var tmp = new Image(nwidth, nheight);

            Graphics.GetPixels(tmp);
            this.Ptr = tmp.Ptr;

            GC.KeepAlive(this);
#else

            #region OpenCv

                        AreaExpansion(nwidth, nheight);

                        //縁取りを描画
                        var mask = Clone();
                        mask.SetColor(color);
                        mask.Dilate(size);
                        var maskoutptr = mask.OutputArray;

                        //HASK ; 加算合成時に終了する場合がある
                        NativeMethods.HandleException(NativeMethods.core_add(mask.InputArray, InputArray, maskoutptr, IntPtr.Zero, 0));

                        NativeMethods.HandleException(NativeMethods.core_Mat_delete(Ptr));
                        Disposable.Dispose();

                        Ptr = mask.Ptr;

                        NativeMethods.HandleException(NativeMethods.core_OutputArray_delete(maskoutptr));

            #endregion
#endif
        }

        #endregion

        #region SetColor

        /// <summary>
        /// 
        /// </summary>
        /// <param name="color"></param>
        public unsafe void SetColor(Color color) {
            ThrowIfDisposed();

            int bitcount = Width * Height * Channels * Type.Bits / 8;

            byte* pixelPtr = DataPointer;
            var step = (int)Step;
            var elemsize = ElemSize;

            Parallel.For(0, Height, y => {
                Parallel.For(0, Width, x => {
                    //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                    //int pos = y * Stride + x * 4;
                    int pos = y * step + x * elemsize;

                    // BGRA
                    pixelPtr[pos] = (byte)color.B;
                    pixelPtr[pos + 1] = (byte)color.G;
                    pixelPtr[pos + 2] = (byte)color.R;
                });
            });

            GC.KeepAlive(this);
        }

        #endregion

        #region Shadow

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="blur"></param>
        /// <param name="alpha"></param>
        /// <param name="color"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void Shadow(in float x, in float y, in int blur, in float alpha, Color color) {
            if (blur < 0) throw new ArgumentException("blur < 0");
            ThrowIfDisposed();

            Image shadow = Clone();
            shadow.Blur(blur, true);
            shadow.SetColor(color);
            ImageHelper.DrawAlpha(shadow, (float)(alpha / 100));

            //キャンバスのサイズ
            int size_w = (int)((Math.Abs(x) + (shadow.Width / 2)) * 2);
            int size_h = (int)((Math.Abs(x) + (shadow.Height / 2)) * 2);

#if UseOpenGL
            ImageHelper.renderer.Clear(size_w, size_h);
            Graphics.Paint(new Point3(x, y, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(shadow));
            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(this));

            shadow.Dispose();

            Native.ImageProcess.Delete(ptr);
            Disposable.Dispose();

            Ptr = new Image(size_w, size_h).Ptr;

            Graphics.GetPixels(this);

            GC.KeepAlive(this);
#else
            var canvas = new Image(size_w, size_h);

            canvas.DrawImage(new Point2(x + (size_w / 2), y + (size_h / 2)), shadow); //影の描画
            canvas.DrawImage(new Point2(size_w / 2, size_h / 2), this);

            shadow.Dispose();
            NativeMethods.HandleException(NativeMethods.core_Mat_delete(Ptr));
            Disposable.Dispose();

            Ptr = canvas.Ptr;

            GC.KeepAlive(this);
#endif
        }

        #endregion

        #region Dilate

        /// <summary>
        /// 
        /// </summary>
        /// <param name="f"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void Dilate(in int f) {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0) {
                ImageProcess.Delete(ptr);
                Disposable.Dispose();

                Ptr = new Image().Ptr;
                return;
            }
            ThrowIfDisposed();

            var result = ImageProcess.Dilate(ptr, f);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        #endregion

        #region Erode

        /// <summary>
        /// 
        /// </summary>
        /// <param name="f"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="NativeException"/>
        public void Erode(in int f) {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0) {
                ImageProcess.Delete(ptr);
                Disposable.Dispose();

                Ptr = new Image().Ptr;
                return;
            }
            ThrowIfDisposed();

            var result = ImageProcess.Erode(ptr, f);

            if (result != null) throw new NativeException(result);

            GC.KeepAlive(this);
        }

        #endregion

        #region Clip

        /// <summary>
        /// 
        /// </summary>
        /// <param name="top"></param>
        /// <param name="bottom"></param>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void Clip(in int top, in int bottom, in int left, in int right) {
            if (top < 0 || bottom < 0 || left < 0 || right < 0) throw new ArgumentException();
            ThrowIfDisposed();
            if (Width < left + right || Height < top + bottom) {
                var ptr = new Image().Ptr;
                ImageProcess.Delete(ptr);
                Disposable.Dispose();
                Ptr = ptr;
                return;
            }

            int width = Width - left - right;
            int height = Height - top - bottom;
            int x = left;
            int y = top;

            var tmp = Clone(new Rectangle(x, y, width, height));
            ImageProcess.Delete(ptr);
            Disposable.Dispose();

            Ptr = tmp.Ptr;

            GC.KeepAlive(this);
        }

        #endregion

        #region Draw

        public void DrawImage(Point2 point, Image image) {
            if (image is null) {
                throw new ArgumentNullException(nameof(image));
            }
            ThrowIfDisposed();
            image.ThrowIfDisposed();

            var rect = new Rectangle(point, image.Size);
            var a = this[rect];

            Native.ImageProcess.Add(a.Ptr, image.Ptr);

            this[rect] = a;

            GC.KeepAlive(this);
        }

        #endregion

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as Image);
        /// <inheritdoc/>
        public bool Equals(Image other) => other != null && Ptr.Equals(other.Ptr);
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Ptr);
        /// <inheritdoc/>
        public override string ToString() {
            if (Ptr == IntPtr.Zero) return base.ToString();
            return $"(Width:{Width} Height:{Height} Type:{Type} Data:{Data})";
        }

        internal DisposableCollection Disposable { get; } = new DisposableCollection();
        protected override void OnDispose(bool disposing) {
            if (ptr != IntPtr.Zero && !IsDisposed) {
                Native.ImageProcess.Delete(ptr);
                Disposable.Dispose();
            }

            ptr = IntPtr.Zero;
        }

        /// <inheritdoc/>
        public static bool operator ==(Image left, Image right) => EqualityComparer<Image>.Default.Equals(left, right);
        /// <inheritdoc/>
        public static bool operator !=(Image left, Image right) => !(left == right);
    }

    public enum FlipMode {
        XY = -1,
        X = 0,
        Y = 1
    }
}

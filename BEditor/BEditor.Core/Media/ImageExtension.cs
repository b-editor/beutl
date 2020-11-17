#define UseOpenGL

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Exceptions;
using BEditor.Core.Graphics;
using BEditor.Core.Native;
using BEditor.Core.Renderings;

namespace BEditor.Core.Media
{
    public unsafe static class ImageExtension
    {
        public static Image Flip(this Image image, FlipMode mode)
        {
            image.ThrowIfDisposed();

            var result = ImageProcess.Flip(image.Ptr, (int)mode);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static Image AreaExpansion(this Image image, int top, int bottom, int left, int right)
        {
            image.ThrowIfDisposed();
            var result = ImageProcess.AreaExpansion(image.Ptr, top, bottom, left, right);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static Image AreaExpansion(this Image image, int width, int height)
        {
            image.ThrowIfDisposed();

            int v = (height - image.Height) / 2;
            int h = (width - image.Width) / 2;

            image.AreaExpansion(v, v, h, h);

            return image;
        }

        public static Image Blur(this Image image, int blurSize, bool alphaBlur)
        {
            image.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blursize < 0");
            if (blurSize == 0) return image;

            var result = ImageProcess.Blur(image.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static Image GaussianBlur(this Image image,int blurSize, bool alphaBlur)
        {
            image.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0");
            if (blurSize == 0) return image;

            var result = ImageProcess.GaussianBlur(image.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static Image MedianBlur(this Image image, int blurSize, bool alphaBlur)
        {
            image.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0", nameof(blurSize));
            if (blurSize == 0) return image;

            var result = ImageProcess.MedianBlur(image.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static Image Border(this Image image, int size, Color color)
        {
            if (size <= 0) throw new ArgumentException("size <= 0");
            image.ThrowIfDisposed();

            int nwidth = image.Width + (size + 5) * 2;
            int nheight = image.Height + (size + 5) * 2;

#if UseOpenGL
            ImageHelper.renderer.Resize(nwidth, nheight);


            //縁取りを描画
            var mask = image.Clone();
            mask.SetColor(color);
            mask.AreaExpansion(nwidth, nheight);
            mask.Dilate(size);

            GLTK.Paint(Point3.Empty, 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(mask));

            mask.Dispose();
            GLTK.Paint(Point3.Empty, 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(image));


            ImageProcess.Release(image.Ptr);

            var tmp = new Image(nwidth, nheight);

            GLTK.GetPixels(tmp);
            image.Ptr = tmp.Ptr;
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
            return image;
        }

        public static Image SetColor(this Image image, Color color)
        {
            image.ThrowIfDisposed();

            int bitcount = image.Width * image.Height * image.Channels * image.Type.Bits / 8;

            byte* pixelPtr = image.DataPointer;
            var step = (int)image.Step;
            var elemsize = image.ElemSize;

            Parallel.For(0, image.Height, y =>
            {
                Parallel.For(0, image.Width, x =>
                {
                    //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                    //int pos = y * Stride + x * 4;
                    int pos = y * step + x * elemsize;

                    // BGRA
                    pixelPtr[pos] = (byte)color.B;
                    pixelPtr[pos + 1] = (byte)color.G;
                    pixelPtr[pos + 2] = (byte)color.R;
                });
            });

            return image;
        }

        public static Image Shadow(this Image image, float x, float y, int blur, float alpha, Color color)
        {
            if (blur < 0) throw new ArgumentException("blur < 0");
            image.ThrowIfDisposed();

            Image shadow = image.Clone();
            shadow.Blur(blur, true);
            shadow.SetColor(color);
            ImageHelper.DrawAlpha(shadow, (float)(alpha / 100));

            //キャンバスのサイズ
            int size_w = (int)((Math.Abs(x) + (shadow.Width / 2)) * 2);
            int size_h = (int)((Math.Abs(x) + (shadow.Height / 2)) * 2);

#if UseOpenGL
            ImageHelper.renderer.Resize(size_w, size_h);
            GLTK.Paint(new Point3(x, y, 0), 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(shadow));
            GLTK.Paint(Point3.Empty, 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(image));

            shadow.Dispose();

            ImageProcess.Release(image.Ptr);

            image.Ptr = new Image(size_w, size_h).Ptr;

            GLTK.GetPixels(image);
#else
            var canvas = new Image(size_w, size_h);

            canvas.DrawImage(new Point2(x + (size_w / 2), y + (size_h / 2)), shadow); //影の描画
            canvas.DrawImage(new Point2(size_w / 2, size_h / 2), this);

            shadow.Dispose();
            NativeMethods.HandleException(NativeMethods.core_Mat_delete(Ptr));
            Disposable.Dispose();

            Ptr = canvas.Ptr;

            //GC.KeepAlive(this);
#endif
            return image;
        }

        public static Image Dilate(this Image image, int f)
        {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0)
            {
                ImageProcess.Release(image.Ptr);

                image.Ptr = new Image().Ptr;
                return image;
            }
            image.ThrowIfDisposed();

            var result = ImageProcess.Dilate(image.Ptr, f);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static Image Erode(this Image image, int f)
        {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0)
            {
                ImageProcess.Release(image.Ptr);

                image.Ptr = new Image().Ptr;
                return image;
            }
            image.ThrowIfDisposed();

            var result = ImageProcess.Erode(image.Ptr, f);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static Image Clip(this Image image, int top, int bottom, int left, int right)
        {
            if (top < 0 || bottom < 0 || left < 0 || right < 0) throw new ArgumentException();
            image.ThrowIfDisposed();
            if (image.Width <= left + right || image.Height <= top + bottom)
            {
                ImageProcess.Release(image.Ptr);
                var ptr = new Image(1, 1, ImageType.ByteCh4).Ptr;
                image.Ptr = ptr;
                return image;
            }

            ImageProcess.Clip(image.Ptr, top, bottom, left, right, out var ptr_);
            image.Ptr = ptr_;

            return image;
        }

    }
}

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
        public static Image GaussianBlur(this Image image, int blurSize, bool alphaBlur)
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

            ImageHelper.renderer.Resize(nwidth, nheight);


            //縁取りを描画
            var mask = image.Clone()
                .SetColor(color)
                .AreaExpansion(nwidth, nheight)
                .Dilate(size);

            GLTK.Paint(Point3.Empty, 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(mask));

            mask.Dispose();
            GLTK.Paint(Point3.Empty, 0, 0, 0, Point3.Empty, () => GLTK.DrawImage(image));


            ImageProcess.Delete(image.Ptr);

            var tmp = new Image(nwidth, nheight, ImageType.ByteCh4);

            GLTK.GetPixels(tmp);
            image.Ptr = tmp.Ptr;

            GC.SuppressFinalize(tmp);

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

            Image shadow = image
                .Clone()
                .Blur(blur, true)
                .SetColor(color)
                .DrawAlpha((float)(alpha / 100));

            //キャンバスのサイズ
            int size_w = (int)((Math.Abs(x) + (shadow.Width / 2)) * 2);
            int size_h = (int)((Math.Abs(x) + (shadow.Height / 2)) * 2);


            ImageHelper.renderer.Resize(size_w, size_h);

            shadow
                .SetCoord(x, y,0)
                .Render(BaseGraphicsContext.Default)
                .Dispose();

            image
                .Render(BaseGraphicsContext.Default);

            ImageProcess.Delete(image.Ptr);

            var tmp = new Image(size_w, size_h, ImageType.ByteCh4);
            image.Ptr = tmp.Ptr;

            GLTK.GetPixels(image);

            GC.SuppressFinalize(tmp);

            return image;
        }

        public static Image Dilate(this Image image, int f)
        {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0)
            {
                ImageProcess.Delete(image.Ptr);

                image.Ptr = Image.UnmanageAlloc();
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
                ImageProcess.Delete(image.Ptr);

                image.Ptr = Image.UnmanageAlloc();
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
                ImageProcess.Delete(image.Ptr);
                var ptr = Image.UnmanageAlloc();
                image.Ptr = ptr;
                return image;
            }

            ImageProcess.Clip(image.Ptr, top, bottom, left, right, out var ptr_);
            image.Ptr = ptr_;

            return image;
        }

        public static Image DrawAlpha(this Image image, float alpha)
        {
            ImageHelper.DrawAlpha(image, alpha);
            return image;
        }



        public static Image SetCoord(this Image image, Point3 coord)
        {
            image.Coord = coord;
            return image;
        }
        public static Image SetCoord(this Image image, float x, float y, float z)
        {
            image.Coord = new Point3(x, y, z);
            return image;
        }
        public static Image SetCoord(this Image image, int x, int y, int z)
        {
            image.Coord = new Point3(x, y, z);
            return image;
        }

        public static Image SetCenter(this Image image, Point3 center)
        {
            image.Center = center;
            return image;
        }
        public static Image SetCenter(this Image image, float cx, float cy, float cz)
        {
            image.Center = new Point3(cx, cy, cz);
            return image;
        }
        public static Image SetCenter(this Image image, int cx, int cy, int cz)
        {
            image.Center = new Point3(cx, cy, cz);
            return image;
        }

        public static Image SetRotate(this Image image, Point3 rotate)
        {
            image.Rotate = rotate;
            return image;
        }
        public static Image SetRotate(this Image image, float rx, float ry, float rz)
        {
            image.Rotate = new Point3(rx, ry, rz);
            return image;
        }
        public static Image SetRotate(this Image image, int rx, int ry, int rz)
        {
            image.Rotate = new Point3(rx, ry, rz);
            return image;
        }

        public static Image SetScale(this Image image, Point3 scale)
        {
            image.Scale = scale;
            return image;
        }
        public static Image SetScale(this Image image, float sx, float sy, float sz)
        {
            image.Scale = new Point3(sx, sy, sz);
            return image;
        }

        public static Image SetMaterial(this Image image, MaterialRecord material)
        {
            image.Material = material;
            return image;
        }
    }
}

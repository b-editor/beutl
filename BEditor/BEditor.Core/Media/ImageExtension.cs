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
        public static IRenderable<Image> Flip(this IRenderable<Image> image, FlipMode mode)
        {
            image.Source.ThrowIfDisposed();

            var result = ImageProcess.Flip(image.Source.Ptr, (int)mode);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static IRenderable<Image> AreaExpansion(this IRenderable<Image> image, int top, int bottom, int left, int right)
        {
            image.Source.ThrowIfDisposed();
            var result = ImageProcess.AreaExpansion(image.Source.Ptr, top, bottom, left, right);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static IRenderable<Image> AreaExpansion(this IRenderable<Image> image, int width, int height)
        {
            image.Source.ThrowIfDisposed();

            int v = (height - image.Source.Height) / 2;
            int h = (width - image.Source.Width) / 2;

            image.AreaExpansion(v, v, h, h);

            return image;
        }

        public static IRenderable<Image> Blur(this IRenderable<Image> image, int blurSize, bool alphaBlur)
        {
            image.Source.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blursize < 0");
            if (blurSize == 0) return image;

            var result = ImageProcess.Blur(image.Source.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static IRenderable<Image> GaussianBlur(this IRenderable<Image> image, int blurSize, bool alphaBlur)
        {
            image.Source.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0");
            if (blurSize == 0) return image;

            var result = ImageProcess.GaussianBlur(image.Source.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static IRenderable<Image> MedianBlur(this IRenderable<Image> image, int blurSize, bool alphaBlur)
        {
            image.Source.ThrowIfDisposed();

            if (blurSize < 0) throw new ArgumentException("blurSize < 0", nameof(blurSize));
            if (blurSize == 0) return image;

            var result = ImageProcess.MedianBlur(image.Source.Ptr, blurSize, alphaBlur);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static IRenderable<Image> Border(this IRenderable<Image> image, int size, in ReadOnlyColor color)
        {
            if (size <= 0) throw new ArgumentException("size <= 0");
            image.Source.ThrowIfDisposed();

            int nwidth = image.Source.Width + (size + 5) * 2;
            int nheight = image.Source.Height + (size + 5) * 2;

            GraphicsContext.Default.Resize(nwidth, nheight);


            // 縁を描画
            image.Source.Clone()
                .ToRenderable()
                .SetColor(color)
                .AreaExpansion(nwidth, nheight)
                .Dilate(size)
                // 描画
                .Render(GraphicsContext.Default)
                .Dispose();

            image
                .ResetProperties()
                .Render(GraphicsContext.Default);


            ImageProcess.Delete(image.Source.Ptr);

            var tmp = new Image(nwidth, nheight, ImageType.ByteCh4);

            GLTK.GetPixels(tmp);
            image.Source.Ptr = tmp.Ptr;

            GC.SuppressFinalize(tmp);

            return image;
        }

        public static IRenderable<Image> SetColor(this IRenderable<Image> image, ReadOnlyColor color)
        {
            image.Source.ThrowIfDisposed();

            int bitcount = image.Source.Width * image.Source.Height * image.Source.Channels * image.Source.Type.Bits / 8;

            byte* pixelPtr = image.Source.DataPointer;
            var step = (int)image.Source.Step;
            var elemsize = image.Source.ElemSize;

            Parallel.For(0, image.Source.Height, y =>
            {
                Parallel.For(0, image.Source.Width, x =>
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

        public static IRenderable<Image> Shadow(this IRenderable<Image> image, float x, float y, int blur, float alpha, in ReadOnlyColor color)
        {
            if (blur < 0) throw new ArgumentException("blur < 0");
            image.Source.ThrowIfDisposed();

            var shadow = image.Source.Clone().ToRenderable()
                .Blur(blur, true)
                .SetColor(color)
                .DrawAlpha((float)(alpha / 100));

            //キャンバスのサイズ
            int size_w = (int)((Math.Abs(x) + (shadow.Source.Width / 2)) * 2);
            int size_h = (int)((Math.Abs(x) + (shadow.Source.Height / 2)) * 2);


            GraphicsContext.Default.Resize(size_w, size_h);

            shadow
                .SetCoord(x, y, 0)
                .Render(GraphicsContext.Default)
                .Dispose();

            image
                .Render(GraphicsContext.Default);

            ImageProcess.Delete(image.Source.Ptr);

            var tmp = new Image(size_w, size_h, ImageType.ByteCh4);
            image.Source.Ptr = tmp.Ptr;

            GLTK.GetPixels(image.Source);

            GC.SuppressFinalize(tmp);

            return image;
        }

        public static IRenderable<Image> Dilate(this IRenderable<Image> image, int f)
        {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0)
            {
                ImageProcess.Delete(image.Source.Ptr);

                image.Source.Ptr = Image.UnmanageAlloc();
                return image;
            }
            image.Source.ThrowIfDisposed();

            var result = ImageProcess.Dilate(image.Source.Ptr, f);

            if (result != null) throw new NativeException(result);

            return image;
        }
        public static IRenderable<Image> Erode(this IRenderable<Image> image, int f)
        {
            if (f < 0) throw new ArgumentException("f < 0");
            if (f == 0)
            {
                ImageProcess.Delete(image.Source.Ptr);

                image.Source.Ptr = Image.UnmanageAlloc();
                return image;
            }
            image.Source.ThrowIfDisposed();

            var result = ImageProcess.Erode(image.Source.Ptr, f);

            if (result != null) throw new NativeException(result);

            return image;
        }

        public static IRenderable<Image> Clip(this IRenderable<Image> image, int top, int bottom, int left, int right)
        {
            if (top < 0 || bottom < 0 || left < 0 || right < 0) throw new ArgumentException();
            image.Source.ThrowIfDisposed();
            if (image.Source.Width <= left + right || image.Source.Height <= top + bottom)
            {
                ImageProcess.Delete(image.Source.Ptr);
                var ptr = Image.UnmanageAlloc();
                image.Source.Ptr = ptr;
                return image;
            }

            ImageProcess.Clip(image.Source.Ptr, top, bottom, left, right, out var ptr_);
            image.Source.Ptr = ptr_;

            return image;
        }

        public static IRenderable<Image> DrawAlpha(this IRenderable<Image> image, float alpha)
        {
            image.Source.ThrowIfDisposed();
            //int bitcount = Width * Height * Channels * Type.Bits / 8;

            byte* pixelPtr = image.Source.DataPointer;
            var step = (int)image.Source.Step;
            var elemsize = image.Source.ElemSize;


            Parallel.For(0, image.Source.Height, y =>
            {
                Parallel.For(0, image.Source.Width, x =>
                {
                    //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                    int pos = y * step + x * elemsize;

                    // BGRA
                    pixelPtr[pos + 3] = (byte)(pixelPtr[pos + 3] * alpha);
                });
            });

            return image;
        }

        public static IRenderable<Image> ToRenderable(this Image image) => new RenderableImage() { Source = image };

        #region Coord

        public static IRenderable<Image> SetCoord(this IRenderable<Image> image, Point3 coord)
        {
            image.Source.Coord = coord;
            return image;
        }
        public static IRenderable<Image> SetCoord(this IRenderable<Image> image, float x, float y, float z)
        {
            image.Source.Coord = new(x, y, z);
            return image;
        }
        public static IRenderable<Image> SetCoord(this IRenderable<Image> image, int x, int y, int z)
        {
            image.Source.Coord = new(x, y, z);
            return image;
        }
        public static IRenderable<Image> GetCoord(this IRenderable<Image> image, out Point3 coord)
        {
            coord = image.Source.Coord;
            return image;
        }
        public static IRenderable<Image> GetCoord(this IRenderable<Image> image, out float x, out float y, out float z)
        {
            x = image.Source.Coord.X;
            y = image.Source.Coord.Y;
            z = image.Source.Coord.Z;
            return image;
        }
        public static IRenderable<Image> GetCoord(this IRenderable<Image> image, out int x, out int y, out int z)
        {
            x = (int)image.Source.Coord.X;
            y = (int)image.Source.Coord.Y;
            z = (int)image.Source.Coord.Z;
            return image;
        }

        #endregion

        #region Center

        public static IRenderable<Image> SetCenter(this IRenderable<Image> image, Point3 center)
        {
            image.Source.Center = center;
            return image;
        }
        public static IRenderable<Image> SetCenter(this IRenderable<Image> image, float cx, float cy, float cz)
        {
            image.Source.Center = new(cx, cy, cz);
            return image;
        }
        public static IRenderable<Image> SetCenter(this IRenderable<Image> image, int cx, int cy, int cz)
        {
            image.Source.Center = new(cx, cy, cz);
            return image;
        }
        public static IRenderable<Image> GetCenter(this IRenderable<Image> image, out Point3 center)
        {
            center = image.Source.Center;
            return image;
        }
        public static IRenderable<Image> GetCenter(this IRenderable<Image> image, out float cx, out float cy, out float cz)
        {
            cx = image.Source.Center.X;
            cy = image.Source.Center.Y;
            cz = image.Source.Center.Z;
            return image;
        }
        public static IRenderable<Image> GetCenter(this IRenderable<Image> image, out int cx, out int cy, out int cz)
        {
            cx = (int)image.Source.Center.X;
            cy = (int)image.Source.Center.Y;
            cz = (int)image.Source.Center.Z;
            return image;
        }

        #endregion

        #region Rotate

        public static IRenderable<Image> SetRotate(this IRenderable<Image> image, Point3 rotate)
        {
            image.Source.Rotate = rotate;
            return image;
        }
        public static IRenderable<Image> SetRotate(this IRenderable<Image> image, float rx, float ry, float rz)
        {
            image.Source.Rotate = new(rx, ry, rz);
            return image;
        }
        public static IRenderable<Image> SetRotate(this IRenderable<Image> image, int rx, int ry, int rz)
        {
            image.Source.Rotate = new(rx, ry, rz);
            return image;
        }
        public static IRenderable<Image> GetRotate(this IRenderable<Image> image, out Point3 rotate)
        {
            rotate = image.Source.Rotate;
            return image;
        }
        public static IRenderable<Image> GetRotate(this IRenderable<Image> image, out float rx, out float ry, out float rz)
        {
            rx = image.Source.Rotate.X;
            ry = image.Source.Rotate.Y;
            rz = image.Source.Rotate.Z;
            return image;
        }
        public static IRenderable<Image> GetRotate(this IRenderable<Image> image, out int rx, out int ry, out int rz)
        {
            rx = (int)image.Source.Rotate.X;
            ry = (int)image.Source.Rotate.Y;
            rz = (int)image.Source.Rotate.Z;
            return image;
        }

        #endregion

        #region Scele

        public static IRenderable<Image> SetScale(this IRenderable<Image> image, Point3 scale)
        {
            image.Source.Scale = scale;
            return image;
        }
        public static IRenderable<Image> SetScale(this IRenderable<Image> image, float sx, float sy, float sz)
        {
            image.Source.Scale = new Point3(sx, sy, sz);
            return image;
        }
        public static IRenderable<Image> GetScale(this IRenderable<Image> image, out Point3 scale)
        {
            scale = image.Source.Scale;
            return image;
        }
        public static IRenderable<Image> GetScale(this IRenderable<Image> image, out float sx, out float sy, out float sz)
        {
            sx = image.Source.Scale.X;
            sy = image.Source.Scale.Y;
            sz = image.Source.Scale.Z;
            return image;
        }

        #endregion

        #region Material

        public static IRenderable<Image> SetMaterial(this IRenderable<Image> image, MaterialRecord material)
        {
            image.Source.Material = material;
            return image;
        }
        public static IRenderable<Image> GetMaterial(this IRenderable<Image> image, out MaterialRecord material)
        {
            material = image.Source.Material;
            return image;
        }

        #endregion

        public static IRenderable<Image> ResetProperties(this IRenderable<Image> image)
        {
            image.Source.Center = new(0, 0, 0);
            image.Source.Coord = new(0, 0, 0);
            image.Source.Scale = new(1, 1, 1);
            image.Source.Rotate = new(0, 0, 0);
            image.Source.Material = new(
                new(1f, 1f, 1f, 1f),
                new(1f, 1f, 1f, 1f),
                new(1f, 1f, 1f, 1f),
                 16f,
                new(1f, 1f, 1f, 1f));

            return image;
        }
    }
}

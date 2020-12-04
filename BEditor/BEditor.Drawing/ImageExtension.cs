using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing
{
    public unsafe static partial class Image
    {
        public static void DrawImage(this Image<BGRA32> self, Point point, Image<BGRA32> image)
        {
            var rect = new Rectangle(point, image.Size);
            var blended = self[rect];

            fixed (BGRA32* dst = blended.Data)
            fixed (BGRA32* src = image.Data)
            {
                var proc = new AlphaBlendProcess(dst, src);
                Parallel.For(0, image.Length, proc.Invoke);
            }

            self[rect] = blended;
        }
        public static Bitmap ToBitmap(this Image<BGRA32> self)
        {
            var width = self.Width;
            var height = self.Height;

            var result = new Bitmap(width, height);
            var data = result.LockBits(new(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            fixed (BGRA32* srcData = self.Data)
            {
                Buffer.MemoryCopy(srcData, (void*)data.Scan0, self.DataSize, self.DataSize);
            }

            result.UnlockBits(data);

            return result;
        }
        public static Image<BGRA32> ToImage(this Bitmap self)
        {
            var data = self.LockBits(new(0, 0, self.Width, self.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var result = new Image<BGRA32>(data.Width, data.Height, data.Scan0);

            self.UnlockBits(data);

            return result;
        }

        public static void SetAlpha(this Image<BGRA32> self, float alpha)
        {
            self.ThrowIfDisposed();

            fixed(BGRA32* data = self.Data)
            {
                var p = new SetAlphaProcess(data, alpha);
                Parallel.For(0, self.Length, p.Invoke);
            }
        }
        public static void SetColor(this Image<BGRA32> self, BGRA32 color)
        {
            fixed(BGRA32* data = self.Data)
            {
                var p = new SetColorProcess(data, color);
                Parallel.For(0, self.Length, p.Invoke);
            }
        }
        public static Image<BGRA32> Border(this Image<BGRA32> self, int size, BGRA32 color)
        {
            if (size <= 0) throw new ArgumentException("size <= 0");
            self.ThrowIfDisposed();

            int nwidth = self.Width + (size + 5) * 2;
            int nheight = self.Height + (size + 5) * 2;
            var result = new Image<BGRA32>(nwidth, nheight);

            // 縁を描画
            var border = self.Clone();
            border.Fill(color);
            border = border.MakeBorder(nwidth, nheight).Dilate(size);

            result.DrawImage(Point.Empty, border);
            
            var x = nwidth / 2 - self.Width / 2;
            var y = nheight / 2 - self.Height / 2;

            result.DrawImage(new Point(x, y), self);

            return result;
        }
        public static Image<BGRA32> Shadow(this Image<BGRA32> self, int x, int y, int blur, float alpha, BGRA32 color)
        {
            if (blur < 0) throw new ArgumentException("blur < 0");
            self.ThrowIfDisposed();

            var shadow = self.Clone();
            var w = shadow.Width + blur;
            var h = shadow.Height + blur;
            shadow = shadow.MakeBorder(w, h);
            shadow.SetColor(color);
            shadow.SetAlpha(alpha);

            //キャンバスのサイズ
            var size_w = (Math.Abs(x) + (shadow.Width / 2)) * 2;
            var size_h = (Math.Abs(x) + (shadow.Height / 2)) * 2;

            var result = new Image<BGRA32>(size_w, size_h);

            result.DrawImage(
                new(
                    (result.Width / 2 - shadow.Width / 2) + x,
                    (result.Height / 2 - shadow.Height / 2) + y),
                shadow);
            
            result.DrawImage(
                new(
                    (result.Width / 2 - self.Width / 2) + x,
                    (result.Height / 2 - self.Height / 2) + y),
                self);

            shadow.Dispose();

            return result;
        }

        private unsafe readonly struct SetAlphaProcess
        {
            private readonly BGRA32* data;
            private readonly float alpha;

            public SetAlphaProcess(BGRA32* data, float alpha)
            {
                this.data = data;
                this.alpha = alpha;
            }

            public readonly void Invoke(int pos)
            {
                data[pos].A = (byte)(data[pos].A * alpha);
            }
        }
        private unsafe readonly struct SetColorProcess
        {
            private readonly BGRA32* data;
            private readonly BGRA32 color;

            public SetColorProcess(BGRA32* data, BGRA32 color)
            {
                this.data = data;
                this.color = color;
            }

            public readonly void Invoke(int pos)
            {
                data[pos].B = color.B;
                data[pos].G = color.G;
                data[pos].R = color.R;
            }
        }
        private unsafe readonly struct AlphaBlendProcess
        {
            readonly BGRA32* dst;
            readonly BGRA32* src;

            public AlphaBlendProcess(BGRA32* dst, BGRA32* src)
            {
                this.dst = dst;
                this.src = src;
            }

            public readonly void Invoke(int pos)
            {
                var srcA = src[pos].A;

                if (srcA is 0) return;

                var dstA = dst[pos].A;
                var blendA = (srcA + dstA) - srcA * dstA / 255;

                dst[pos].B = (byte)((src[pos].B * srcA + dst[pos].B * (255 - srcA) * dstA / 255) / blendA);
                dst[pos].G = (byte)((src[pos].G * srcA + dst[pos].G * (255 - srcA) * dstA / 255) / blendA);
                dst[pos].R = (byte)((src[pos].R * srcA + dst[pos].R * (255 - srcA) * dstA / 255) / blendA);
            }
        }
    }
}

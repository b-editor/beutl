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
        public static void DrawImage(this Image<BGRA32> self, Point2 point, Image<BGRA32> image)
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

        private unsafe readonly struct AlphaBlendProcess
        {
            readonly BGRA32* dst;
            readonly BGRA32* src;

            public AlphaBlendProcess(BGRA32* dst, BGRA32* src)
            {
                this.dst = dst;
                this.src = src;
            }

            public void Invoke(int pos)
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

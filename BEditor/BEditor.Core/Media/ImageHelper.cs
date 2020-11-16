using System;
using System.Threading.Tasks;

using BEditor.Core.Rendering;
using BEditor.Rendering;

namespace BEditor.Media
{
    public static class ImageHelper
    {
        #region 透明度
        public static void DrawAlpha(Media.Image image, float alpha)
        {
            int bitcount = image.Width * image.Height * 4;
            int width = image.Width;
            int height = image.Height;
            var stride = width * 4;

            unsafe
            {
                byte* pixelPtr = (byte*)image.Data;

                Parallel.For(0, height, y =>
                {
                    Parallel.For(0, width, x =>
                    {
                        //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                        int pos = y * stride + x * 4;

                        pixelPtr[pos + 3] = (byte)(pixelPtr[pos + 3] * alpha);
                    });
                });
            }
        }
        #endregion

        internal static BaseGraphicsContext renderer;
    }
}
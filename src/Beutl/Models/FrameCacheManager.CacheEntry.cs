using System.Diagnostics.CodeAnalysis;

using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using OpenCvSharp;

namespace Beutl.Models;

public partial class FrameCacheManager
{
    private class CacheEntry : IDisposable
    {
        private Mat _mat;
        private int _bottom;
        private int _right;

        public CacheEntry(Ref<Bitmap<Bgra8888>> bitmap, FrameCacheOptions options)
        {
            SetBitmap(bitmap, options);
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount => (int)(_mat.DataEnd - _mat.DataStart);

        public bool IsLocked { get; set; }

        [MemberNotNull(nameof(_mat))]
        public void SetBitmap(Ref<Bitmap<Bgra8888>> bitmap, FrameCacheOptions options)
        {
            _mat?.Dispose();
            using (Ref<Bitmap<Bgra8888>> t = bitmap.Clone())
            {
                (_mat, _bottom, _right) = ToMat(t.Value, options);
            }

            LastAccessTime = DateTime.UtcNow;
        }

        public Ref<Bitmap<Bgra8888>> GetBitmap()
        {
            LastAccessTime = DateTime.UtcNow;
            return Ref<Bitmap<Bgra8888>>.Create(ToBitmap(_mat, _bottom, _right));
        }

        private static unsafe (Mat, int Bottom, int Right) ToMat(Bitmap<Bgra8888> bitmap, FrameCacheOptions options)
        {
            var result = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);
            Buffer.MemoryCopy((void*)bitmap.Data, (void*)result.Data, bitmap.ByteCount, bitmap.ByteCount);

            PixelSize size = new(bitmap.Width, bitmap.Height);
            PixelSize newSize = options.GetSize(size);

            if (newSize.Width < size.Width || newSize.Height < size.Height)
            {
                Mat tmp = result.Resize(new Size(newSize.Width, newSize.Height), interpolation: InterpolationFlags.Area);
                result.Dispose();
                result = tmp;
            }

            int bottom = 0;
            int right = 0;
            if (options.ColorType == FrameCacheColorType.YUV)
            {
                if (result.Width % 2 == 1)
                {
                    right = 1;
                }
                if (result.Height % 2 == 1)
                {
                    bottom = 1;
                }
                if (right != 0 || bottom != 0)
                {
                    Mat tmp = result.CopyMakeBorder(0, bottom, 0, right, BorderTypes.Constant, Scalar.Black);
                    result.Dispose();
                    result = tmp;
                }

                var mat = new Mat((int)(result.Rows * 1.5), result.Cols, MatType.CV_8UC1);
                Cv2.CvtColor(result, mat, ColorConversionCodes.BGRA2YUV_I420);
                result.Dispose();
                result = mat;
            }

            return (result, bottom, right);
        }

        private static unsafe Bitmap<Bgra8888> ToBitmap(Mat mat, int bottom, int right)
        {
            Bitmap<Bgra8888>? bitmap;
            if (mat.Type() == MatType.CV_8UC4)
            {
                bitmap = new Bitmap<Bgra8888>(mat.Width, mat.Height);
                Buffer.MemoryCopy((void*)mat.Data, (void*)bitmap.Data, bitmap.ByteCount, bitmap.ByteCount);
            }
            else
            {
                bitmap = new Bitmap<Bgra8888>(mat.Width, (int)(mat.Height / 1.5));
                using var bgra = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmap.Data);

                Cv2.CvtColor(mat, bgra, ColorConversionCodes.YUV2BGRA_I420);

                if (bottom != 0 || right != 0)
                {
                    Bitmap<Bgra8888> tmp = bitmap[new PixelRect(0, 0, bitmap.Width - right, bitmap.Height - bottom)];
                    bitmap.Dispose();
                    bitmap = tmp;
                }
            }

            return bitmap;
        }

        public void Dispose()
        {
            _mat?.Dispose();
            _mat = null!;
        }
    }
}

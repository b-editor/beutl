using System.Diagnostics.CodeAnalysis;

using Beutl.Media;
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

        public CacheEntry(Ref<Bitmap> bitmap, FrameCacheOptions options)
        {
            SetBitmap(bitmap, options);
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount => (int)(_mat.DataEnd - _mat.DataStart);

        public bool IsLocked { get; set; }

        [MemberNotNull(nameof(_mat))]
        public void SetBitmap(Ref<Bitmap> bitmap, FrameCacheOptions options)
        {
            _mat?.Dispose();
            using Ref<Bitmap> t = bitmap.Clone();
            if (t.Value.ColorType != BitmapColorType.Bgra8888)
            {
                using var converted = t.Value.Convert(BitmapColorType.Bgra8888);
                (_mat, _bottom, _right) = ToMat(converted, options);
            }
            else
            {
                (_mat, _bottom, _right) = ToMat(t.Value, options);
            }

            LastAccessTime = DateTime.UtcNow;
        }

        public Ref<Bitmap> GetBitmap()
        {
            LastAccessTime = DateTime.UtcNow;
            return Ref<Bitmap>.Create(ToBitmap(_mat, _bottom, _right));
        }

        private static unsafe (Mat, int Bottom, int Right) ToMat(Bitmap bitmap, FrameCacheOptions options)
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

        private static unsafe Bitmap ToBitmap(Mat mat, int bottom, int right)
        {
            Bitmap? bitmap;
            if (mat.Type() == MatType.CV_8UC4)
            {
                bitmap = new Bitmap(mat.Width, mat.Height);
                Buffer.MemoryCopy((void*)mat.Data, (void*)bitmap.Data, bitmap.ByteCount, bitmap.ByteCount);
            }
            else
            {
                bitmap = new Bitmap(mat.Width, (int)(mat.Height / 1.5));
                using var bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmap.Data);

                Cv2.CvtColor(mat, bgra, ColorConversionCodes.YUV2BGRA_I420);

                if (bottom != 0 || right != 0)
                {
                    Bitmap tmp = bitmap.ExtractSubset(new PixelRect(0, 0, bitmap.Width - right, bitmap.Height - bottom));
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

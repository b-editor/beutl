using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Models;

public partial class FrameCacheManager
{
    private class CacheEntry : IDisposable
    {
        private byte[] _data;
        private int _dataLength;
        private int _width;
        private int _height;
        private bool _isYuv;

        public CacheEntry(Ref<Bitmap> bitmap, FrameCacheOptions options)
        {
            _data = [];
            SetBitmap(bitmap, options);
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount => _dataLength;

        public bool IsLocked { get; set; }

        [MemberNotNull(nameof(_data))]
        public void SetBitmap(Ref<Bitmap> bitmap, FrameCacheOptions options)
        {
            ReturnBuffer();
            using Ref<Bitmap> t = bitmap.Clone();
            (_data, _dataLength, _width, _height, _isYuv) = ToCacheData(t, options);

            LastAccessTime = DateTime.UtcNow;
        }

        public Ref<Bitmap> GetBitmap()
        {
            LastAccessTime = DateTime.UtcNow;
            return Ref<Bitmap>.Create(ToBitmap());
        }

        private static unsafe (byte[] Data, int DataLength, int Width, int Height, bool IsYuv) ToCacheData(
            Ref<Bitmap> bitmapRef, FrameCacheOptions options)
        {
            var bitmap = bitmapRef.Value;
            PixelSize size = new(bitmap.Width, bitmap.Height);
            PixelSize newSize = options.GetSize(size);
            Bitmap current = bitmap;
            bool ownsCurrentBitmap = false;

            try
            {
                // Resize if needed
                if (newSize.Width < size.Width ||
                    newSize.Height < size.Height ||
                    bitmap.ColorType != BitmapColorType.Bgra8888 ||
                    bitmap.ColorSpace != BitmapColorSpace.Srgb)
                {
                    var newWidth = Math.Min(size.Width, newSize.Width);
                    var newHeight = Math.Min(size.Height, newSize.Height);
                    var resized = current.SKBitmap.Resize(
                        new SKImageInfo(newWidth, newHeight, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()),
                        new SKSamplingOptions(SKFilterMode.Linear));
                    if (resized != null)
                    {
                        var resizedBitmap = new Bitmap(resized);
                        if (ownsCurrentBitmap) bitmapRef.Dispose();
                        current = resizedBitmap;
                        ownsCurrentBitmap = true;
                    }
                }

                if (options.ColorType == FrameCacheColorType.YUV)
                {
                    int w = current.Width;
                    int h = current.Height;
                    int yuvSize = YuvConversion.GetI420BufferSize(w, h);
                    byte[] yuvData = ArrayPool<byte>.Shared.Rent(yuvSize);

                    fixed (byte* yuvPtr = yuvData)
                    {
                        YuvConversion.BgraToI420((byte*)current.Data, current.RowBytes, yuvPtr, w, h);
                    }

                    return (yuvData, yuvSize, w, h, true);
                }
                else
                {
                    int w = current.Width;
                    int h = current.Height;
                    int dstStride = w * 4;
                    int bgraSize = dstStride * h;
                    byte[] bgraData = ArrayPool<byte>.Shared.Rent(bgraSize);
                    int srcStride = current.RowBytes;

                    if (srcStride == dstStride)
                    {
                        Marshal.Copy(current.Data, bgraData, 0, bgraSize);
                    }
                    else
                    {
                        byte* srcBase = (byte*)current.Data;
                        fixed (byte* dstBase = bgraData)
                        {
                            for (int y = 0; y < h; y++)
                            {
                                Buffer.MemoryCopy(
                                    srcBase + (long)y * srcStride,
                                    dstBase + (long)y * dstStride,
                                    dstStride, dstStride);
                            }
                        }
                    }

                    return (bgraData, bgraSize, w, h, false);
                }
            }
            finally
            {
                if (ownsCurrentBitmap) current.Dispose();
            }
        }

        private unsafe Bitmap ToBitmap()
        {
            if (!_isYuv)
            {
                var bitmap = new Bitmap(_width, _height);
                int srcStride = _width * 4;
                int dstStride = bitmap.RowBytes;

                if (srcStride == dstStride)
                {
                    Marshal.Copy(_data, 0, bitmap.Data, _dataLength);
                }
                else
                {
                    fixed (byte* srcBase = _data)
                    {
                        byte* dstBase = (byte*)bitmap.Data;
                        for (int y = 0; y < _height; y++)
                        {
                            Buffer.MemoryCopy(
                                srcBase + (long)y * srcStride,
                                dstBase + (long)y * dstStride,
                                srcStride, srcStride);
                        }
                    }
                }

                return bitmap;
            }
            else
            {
                var bitmap = new Bitmap(_width, _height);
                fixed (byte* yuvPtr = _data)
                {
                    YuvConversion.I420ToBgra(yuvPtr, (byte*)bitmap.Data, bitmap.RowBytes, _width, _height);
                }

                return bitmap;
            }
        }

        private void ReturnBuffer()
        {
            if (_data is { Length: > 0 })
            {
                ArrayPool<byte>.Shared.Return(_data);
            }
        }

        public void Dispose()
        {
            ReturnBuffer();
            _data = null!;
        }
    }
}

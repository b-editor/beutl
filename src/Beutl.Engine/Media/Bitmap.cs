using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

public sealed class Bitmap : ICloneable, IDisposable
{
    private readonly SKBitmap _skBitmap;
    private readonly BitmapColorSpace _colorSpace;
    private readonly bool _ownsData;

    public Bitmap(int width, int height,
        BitmapColorType colorType = BitmapColorType.Bgra8888,
        BitmapAlphaType alphaType = BitmapAlphaType.Unpremul,
        BitmapColorSpace? colorSpace = null)
    {
        ThrowOutOfRange(width, height);
        _colorSpace = colorSpace ?? BitmapColorSpace.Srgb;
        var info = new SKImageInfo(width, height, colorType.ToSKColorType(), alphaType.ToSKAlphaType(), _colorSpace.SKColorSpace);
        _skBitmap = new SKBitmap(info);
        _skBitmap.Erase(SKColor.Empty);
        _ownsData = true;
    }

    public Bitmap(SKBitmap skBitmap)
    {
        ArgumentNullException.ThrowIfNull(skBitmap);
        _skBitmap = skBitmap;
        _colorSpace = BitmapColorSpace.FromSKColorSpace(skBitmap.ColorSpace);
        _ownsData = true;
    }

    internal Bitmap(SKBitmap skBitmap, bool ownsData)
    {
        _skBitmap = skBitmap;
        _colorSpace = BitmapColorSpace.FromSKColorSpace(skBitmap.ColorSpace);
        _ownsData = ownsData;
    }

    ~Bitmap()
    {
        Dispose();
    }

    public int Width => _skBitmap.Width;

    public int Height => _skBitmap.Height;

    public int ByteCount => _skBitmap.ByteCount;

    public int BytesPerPixel => _skBitmap.BytesPerPixel;

    public int RowBytes => _skBitmap.RowBytes;

    public IntPtr Data => _skBitmap.GetPixels();

    public BitmapColorType ColorType => BitmapColorTypeExtensions.FromSKColorType(_skBitmap.ColorType);

    public BitmapAlphaType AlphaType => BitmapAlphaTypeExtensions.FromSKAlphaType(_skBitmap.AlphaType);

    public BitmapColorSpace ColorSpace => _colorSpace;

    public bool IsDisposed { get; private set; }

    internal SKBitmap SKBitmap => _skBitmap;

    public BitmapInfo Info => new(Width, Height, ByteCount, BytesPerPixel, ColorType, AlphaType, _colorSpace);

    public unsafe Span<byte> GetPixelSpan()
    {
        ThrowIfDisposed();
        return new Span<byte>((void*)Data, ByteCount);
    }

    public unsafe Span<T> GetPixelSpan<T>() where T : unmanaged
    {
        ThrowIfDisposed();
        return MemoryMarshal.Cast<byte, T>(new Span<byte>((void*)Data, ByteCount));
    }

    public unsafe Span<byte> GetRow(int y)
    {
        ThrowIfDisposed();
        ThrowRowOutOfRange(y);
        byte* ptr = (byte*)Data + (long)y * RowBytes;
        return new Span<byte>(ptr, Width * BytesPerPixel);
    }

    public unsafe Span<T> GetRow<T>(int y) where T : unmanaged
    {
        ThrowIfDisposed();
        ThrowRowOutOfRange(y);
        byte* ptr = (byte*)Data + (long)y * RowBytes;
        return MemoryMarshal.Cast<byte, T>(new Span<byte>(ptr, Width * BytesPerPixel));
    }

    public Bitmap ExtractSubset(PixelRect roi)
    {
        ThrowIfDisposed();
        ThrowOutOfRange(roi);

        var result = new Bitmap(roi.Width, roi.Height, ColorType, AlphaType, _colorSpace);
        int bytesPerRow = roi.Width * BytesPerPixel;

        unsafe
        {
            byte* srcBase = (byte*)Data;
            byte* dstBase = (byte*)result.Data;
            Parallel.For(0, roi.Height, y =>
            {
                byte* src = srcBase + (long)(y + roi.Y) * RowBytes + (long)roi.X * BytesPerPixel;
                byte* dst = dstBase + (long)y * result.RowBytes;
                Buffer.MemoryCopy(src, dst, bytesPerRow, bytesPerRow);
            });
        }

        return result;
    }

    public void CopyFrom(Bitmap source, PixelRect destRoi)
    {
        ThrowIfDisposed();
        source.ThrowIfDisposed();
        ThrowOutOfRange(destRoi);

        int bytesPerRow = Math.Min(source.Width, destRoi.Width) * BytesPerPixel;

        unsafe
        {
            byte* srcBase = (byte*)source.Data;
            byte* dstBase = (byte*)Data;
            int rowsToCopy = Math.Min(source.Height, destRoi.Height);
            Parallel.For(0, rowsToCopy, y =>
            {
                byte* src = srcBase + (long)y * source.RowBytes;
                byte* dst = dstBase + (long)(y + destRoi.Y) * RowBytes + (long)destRoi.X * BytesPerPixel;
                Buffer.MemoryCopy(src, dst, bytesPerRow, bytesPerRow);
            });
        }
    }

    public static Bitmap FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var skBitmap = SKBitmap.Decode(stream);
        if (skBitmap == null) throw new InvalidOperationException("Failed to decode bitmap from stream.");

        return new Bitmap(skBitmap);
    }

    public static Bitmap FromFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!File.Exists(file)) throw new FileNotFoundException(null, file);

        var skBitmap = SKBitmap.Decode(file);
        if (skBitmap == null) throw new InvalidOperationException($"Failed to decode bitmap from file: {file}");

        return new Bitmap(skBitmap);
    }

    public bool Save(string file, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(file);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? Image.ToImageFormat(file) : format;

        using var stream = new FileStream(file, FileMode.Create);

        // 画像フォーマットはsRGBガンマ前提のため、リニア色空間の場合はsRGBに変換
        if (!_colorSpace.IsSrgb)
        {
            using var srgb = Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
            return srgb._skBitmap.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        return _skBitmap.Encode(stream, (SKEncodedImageFormat)format, quality);
    }

    public bool Save(Stream stream, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? EncodedImageFormat.Png : format;

        // 画像フォーマットはsRGBガンマ前提のため、リニア色空間の場合はsRGBに変換
        if (!_colorSpace.IsSrgb)
        {
            using var srgb = Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
            return srgb._skBitmap.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        return _skBitmap.Encode(stream, (SKEncodedImageFormat)format, quality);
    }

    public Bitmap Convert(BitmapColorType colorType, BitmapAlphaType? alphaType = null, BitmapColorSpace? colorSpace = null)
    {
        ThrowIfDisposed();
        var destAlpha = alphaType ?? AlphaType;
        var destColorSpace = colorSpace ?? _colorSpace;

        var destInfo = new SKImageInfo(Width, Height, colorType.ToSKColorType(), destAlpha.ToSKAlphaType(), destColorSpace.SKColorSpace);
        var destBitmap = new SKBitmap(destInfo);
        using var canvas = new SKCanvas(destBitmap);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(_skBitmap, SKPoint.Empty, paint);

        return new Bitmap(destBitmap);
    }

    public Bitmap Clone()
    {
        ThrowIfDisposed();
        var copy = _skBitmap.Copy();
        return new Bitmap(copy);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        _skBitmap.Erase(SKColor.Empty);
    }

    public unsafe void Flip(FlipMode mode)
    {
        ThrowIfDisposed();
        int rowBytes = Width * BytesPerPixel;

        if (mode is FlipMode.Y or FlipMode.XY)
        {
            byte* basePtr = (byte*)Data;
            int bpp = BytesPerPixel;
            Parallel.For(0, Height, y =>
            {
                var row = new Span<byte>(basePtr + (long)y * RowBytes, rowBytes);
                Span<byte> tmp = stackalloc byte[bpp];
                for (int left = 0, right = Width - 1; left < right; left++, right--)
                {
                    row.Slice(left * bpp, bpp).CopyTo(tmp);
                    row.Slice(right * bpp, bpp).CopyTo(row.Slice(left * bpp, bpp));
                    tmp.CopyTo(row.Slice(right * bpp, bpp));
                }
            });
        }

        if (mode is FlipMode.X or FlipMode.XY)
        {
            byte* basePtr = (byte*)Data;
            Parallel.For(0, Height / 2, top =>
            {
                int bottom = Height - top - 1;
                Span<byte> tmp = stackalloc byte[rowBytes];
                var topSpan = new Span<byte>(basePtr + (long)top * RowBytes, rowBytes);
                var bottomSpan = new Span<byte>(basePtr + (long)bottom * RowBytes, rowBytes);

                topSpan.CopyTo(tmp);
                bottomSpan.CopyTo(topSpan);
                tmp.CopyTo(bottomSpan);
            });
        }
    }

    public Bitmap MakeBorder(int top, int bottom, int left, int right)
    {
        ThrowIfDisposed();

        int width = left + right + Width;
        int height = top + bottom + Height;
        var img = new Bitmap(width, height, ColorType, AlphaType, _colorSpace);

        img.CopyFrom(this, new PixelRect(left, top, Width, Height));

        return img;
    }

    public Bitmap MakeBorder(int width, int height)
    {
        ThrowIfDisposed();

        int v = (height - Height) / 2;
        int h = (width - Width) / 2;

        return MakeBorder(v, v, h, h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Dispose()
    {
        if (!IsDisposed && _ownsData)
        {
            _skBitmap.Dispose();
        }

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    object ICloneable.Clone() => Clone();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowOutOfRange(int width, int height)
    {
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "'width' is less than 0.");
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height), "'height' is less than 0.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowRowOutOfRange(int y)
    {
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y), "'y' is less than 0.");
        else if (y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y), $"'y' is more than or equal to {Height}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowOutOfRange(PixelRect roi)
    {
        if (roi.X < 0 || roi.Y < 0 || roi.Width < 0 || roi.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(roi));
        if (roi.Bottom > Height) throw new ArgumentOutOfRangeException(nameof(roi));
        if (roi.Right > Width) throw new ArgumentOutOfRangeException(nameof(roi));
    }
}

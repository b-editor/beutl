using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.IO;

using SkiaSharp;

namespace Beutl.Media;

public sealed class Bitmap : ICloneable, IDisposable
{
    private readonly SKBitmap _skBitmap;
    private readonly BitmapColorSpace _colorSpace;
    private readonly bool _ownsData;
    private readonly (int Width, int Height, int BytesPerPixel)? _fixedLayout;

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
        _fixedLayout = GetLayout(info);
    }

    public Bitmap(SKBitmap skBitmap)
        : this(skBitmap, ownsData: true, cacheLayout: false)
    {
    }

    private Bitmap(SKBitmap skBitmap, bool ownsData, bool cacheLayout)
    {
        ArgumentNullException.ThrowIfNull(skBitmap);
        SKImageInfo info = skBitmap.Info;
        _skBitmap = skBitmap;
        _colorSpace = BitmapColorSpace.FromSKColorSpace(info.ColorSpace);
        _ownsData = ownsData;
        if (cacheLayout)
            _fixedLayout = GetLayout(info);
    }

    // A public caller can reallocate its SKBitmap. Cache the layout only when Beutl controls the pixel storage.
    internal static Bitmap FromFixedLayout(SKBitmap skBitmap, bool ownsData = true)
        => new(skBitmap, ownsData, cacheLayout: true);

    internal Bitmap(IntPtr data, int width, int height, int rowBytes,
        BitmapColorType colorType, BitmapAlphaType alphaType, BitmapColorSpace? colorSpace)
    {
        ThrowOutOfRange(width, height);
        _colorSpace = colorSpace ?? BitmapColorSpace.Srgb;
        var info = new SKImageInfo(width, height, colorType.ToSKColorType(),
            alphaType.ToSKAlphaType(), _colorSpace.SKColorSpace);
        _skBitmap = new SKBitmap();
        if (!_skBitmap.InstallPixels(info, data, rowBytes))
            throw new InvalidOperationException("Failed to install external pixels into SKBitmap.");
        _ownsData = true;
        _fixedLayout = GetLayout(info);
    }

    ~Bitmap()
    {
        Dispose();
    }

    public int Width => _fixedLayout?.Width ?? _skBitmap.Width;

    public int Height => _fixedLayout?.Height ?? _skBitmap.Height;

    public int ByteCount => _skBitmap.ByteCount;

    public int BytesPerPixel => _fixedLayout?.BytesPerPixel ?? _skBitmap.BytesPerPixel;

    public int RowBytes => _skBitmap.RowBytes;

    public IntPtr Data => _skBitmap.GetPixels();

    public BitmapColorType ColorType => BitmapColorTypeExtensions.FromSKColorType(_skBitmap.ColorType);

    public BitmapAlphaType AlphaType => BitmapAlphaTypeExtensions.FromSKAlphaType(_skBitmap.AlphaType);

    public BitmapColorSpace ColorSpace => _colorSpace;

    public bool IsDisposed { get; private set; }

    internal SKBitmap SKBitmap => _skBitmap;

    // An image over these pixels, without the full copy SKImage.FromBitmap makes of a mutable bitmap. The pixels must
    // stay alive and unchanged until the draw call that reads the image returns: a raster canvas reads them there, and
    // a GPU canvas of a direct context uploads them there.
    internal SKImage? CreateImageWithoutCopy(BitmapColorSpace? colorSpace = null)
    {
        ThrowIfDisposed();
        if (colorSpace is not null)
            return SKImage.FromPixels(_skBitmap.Info.WithColorSpace(colorSpace.SKColorSpace), Data, RowBytes);

        using SKPixmap? pixmap = _skBitmap.PeekPixels();
        return pixmap is not null ? SKImage.FromPixels(pixmap) : SKImage.FromBitmap(_skBitmap);
    }

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
        var layout = _fixedLayout ?? GetLayout(_skBitmap.Info);
        ThrowRowOutOfRange(y, layout.Height);
        byte* ptr = (byte*)Data + (long)y * RowBytes;
        return new Span<byte>(ptr, layout.Width * layout.BytesPerPixel);
    }

    public Span<T> GetRow<T>(int y) where T : unmanaged => MemoryMarshal.Cast<byte, T>(GetRow(y));

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
        if (source.BytesPerPixel != BytesPerPixel)
            throw new ArgumentException($"Pixel format mismatch: source has {source.BytesPerPixel} bytes per pixel, destination has {BytesPerPixel}.", nameof(source));
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

        return FromFixedLayout(skBitmap);
    }

    public static Bitmap FromFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!File.Exists(file)) throw new FileNotFoundException(null, file);

        var skBitmap = SKBitmap.Decode(file);
        if (skBitmap == null) throw new InvalidOperationException($"Failed to decode bitmap from file: {file}");

        return FromFixedLayout(skBitmap);
    }

    public bool Save(string file, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(file);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? Image.ToImageFormat(file) : format;

        using var output = new StagedOutputFile(file);
        using (var stream = new FileStream(output.TemporaryPath, FileMode.CreateNew))
        {
            if (!Save(stream, format, quality))
                return false;
        }

        output.Commit(CancellationToken.None);
        return true;
    }

    public bool Save(Stream stream, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? EncodedImageFormat.Png : format;

        // SKManagedWStream invokes Stream.Write from native code. An I/O exception escaping
        // that callback terminates the process instead of reaching the caller's catch block.
        using SKData? data = Encode(format, quality);
        if (data == null)
            return false;

        data.SaveTo(stream);
        return true;
    }

    private SKData? Encode(EncodedImageFormat format, int quality)
    {
        // 画像フォーマットはsRGBガンマ前提のため、リニア色空間の場合はsRGBに変換
        if (!_colorSpace.IsSrgb)
        {
            using var srgb = Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
            return srgb._skBitmap.Encode((SKEncodedImageFormat)format, quality);
        }

        return _skBitmap.Encode((SKEncodedImageFormat)format, quality);
    }

    public Bitmap Convert(BitmapColorType colorType, BitmapAlphaType? alphaType = null, BitmapColorSpace? colorSpace = null)
    {
        ThrowIfDisposed();
        var destAlpha = alphaType ?? AlphaType;
        var destColorSpace = colorSpace ?? _colorSpace;

        var destInfo = new SKImageInfo(Width, Height, colorType.ToSKColorType(), destAlpha.ToSKAlphaType(), destColorSpace.SKColorSpace);
        var destBitmap = new SKBitmap(destInfo);
        using var canvas = new SKCanvas(destBitmap);
        // Skia applies the dither only where the destination loses precision (e.g. the F16 linear
        // render target down to 8-bit sRGB); conversions that keep or gain precision stay bit-exact.
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src, IsDither = true };
        canvas.DrawBitmap(_skBitmap, SKPoint.Empty, SKSamplingOptions.Default, paint);

        return FromFixedLayout(destBitmap);
    }

    public Bitmap Clone()
    {
        ThrowIfDisposed();
        var copy = _skBitmap.Copy();
        return FromFixedLayout(copy);
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

    private static (int Width, int Height, int BytesPerPixel) GetLayout(SKImageInfo info)
        => (info.Width, info.Height, info.BytesPerPixel);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowRowOutOfRange(int y, int height)
    {
        if (y < 0)
            throw new ArgumentOutOfRangeException(nameof(y), "'y' is less than 0.");
        else if (y >= height)
            throw new ArgumentOutOfRangeException(nameof(y), $"'y' is more than or equal to {height}");
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

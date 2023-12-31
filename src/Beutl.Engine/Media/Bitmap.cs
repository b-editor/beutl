using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.Graphics.Operations;
using Beutl.Media.Pixel;

using SkiaSharp;

namespace Beutl.Media;

public readonly struct BitmapInfo(int width, int height, int byteCount, int pixelSize)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public int ByteCount { get; } = byteCount;

    public int PixelSize { get; } = pixelSize;
}

public unsafe class Bitmap<T> : IBitmap
    where T : unmanaged, IPixel<T>
{
    private readonly bool _requireDispose = true;
    private T* _pointer;

    public Bitmap(int width, int height)
    {
        ThrowOutOfRange(width, height);

        Width = width;
        Height = height;
        _pointer = (T*)NativeMemory.AllocZeroed((nuint)ByteCount);
    }

    public Bitmap(int width, int height, T* data)
    {
        ThrowOutOfRange(width, height);
        if (data == null) throw new ArgumentNullException(nameof(data));

        _requireDispose = false;
        Width = width;
        Height = height;
        _pointer = data;
    }

    public Bitmap(int width, int height, IntPtr data)
    {
        ThrowOutOfRange(width, height);
        if (data == IntPtr.Zero) throw new ArgumentNullException(nameof(data));

        _requireDispose = false;
        Width = width;
        Height = height;
        _pointer = (T*)data;
    }

    ~Bitmap()
    {
        Dispose();
    }

    public int Width { get; }

    public int Height { get; }

    public int ByteCount => Width * Height * PixelSize;

    public int PixelSize => sizeof(T);

    public Span<T> DataSpan => new(_pointer, Width * Height);

    public IntPtr Data => (IntPtr)_pointer;

    public BitmapInfo Info => new(Width, Height, ByteCount, PixelSize);

    public bool IsDisposed { get; private set; }

    public Type PixelType => typeof(T);

    public ref T this[int x, int y]
    {
        get
        {
            ThrowColOutOfRange(x);

            return ref this[y][x];
        }
    }

    public Span<T> this[int y]
    {
        get
        {
            ThrowIfDisposed();
            ThrowRowOutOfRange(y);

            return DataSpan.Slice(y * Width, Width);
        }
        set
        {
            ThrowIfDisposed();
            ThrowRowOutOfRange(y);

            value.CopyTo(DataSpan.Slice(y * Width, Width));
        }
    }

    public Bitmap<T> this[PixelRect roi]
    {
        get
        {
            ThrowIfDisposed();
            ThrowOutOfRange(roi);
            var value = new Bitmap<T>(roi.Width, roi.Height);

            Parallel.For(0, roi.Height, new CropOperation<T>(this, value, roi).Invoke);

            return value;
        }
        set
        {
            ThrowIfDisposed();
            ThrowOutOfRange(roi);

            Parallel.For(0, roi.Height, new ReplaceOperation<T>(value, this, roi).Invoke);
        }
    }

    public static Bitmap<T> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using (var bmp = SKBitmap.Decode(stream))
        {
            var image = bmp.ToBitmap();

            if (default(T) is Bgra8888)
            {
                return (Bitmap<T>)(object)image;
            }

            Bitmap<T>? converted = image.Convert<T>();
            image.Dispose();
            return converted;
        }
    }

    public static Bitmap<T> FromFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!File.Exists(file)) throw new FileNotFoundException(null, file);

        using (var bmp = SKBitmap.Decode(file))
        {
            var image = bmp.ToBitmap();

            if (default(T) is Bgra8888)
            {
                return (Bitmap<T>)(object)image;
            }

            Bitmap<T>? converted = image.Convert<T>();
            image.Dispose();
            return converted;
        }
    }

    public bool Save(string file, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(file);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? Image.ToImageFormat(file) : format;

        if (default(T) is Bgra8888)
        {
            using var bmp = ((Bitmap<Bgra8888>)(object)this).ToSKBitmap();
            using var stream = new FileStream(file, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        else
        {
            using Bitmap<Bgra8888> converted = Convert<Bgra8888>();
            using var bmp = converted.ToSKBitmap();
            using var stream = new FileStream(file, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
    }

    public bool Save(Stream stream, EncodedImageFormat format = EncodedImageFormat.Default, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfDisposed();
        format = format == EncodedImageFormat.Default ? EncodedImageFormat.Png : format;

        if (default(T) is Bgra8888)
        {
            using var bmp = ((Bitmap<Bgra8888>)(object)this).ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        else
        {
            using Bitmap<Bgra8888> converted = Convert<Bgra8888>();
            using var bmp = converted.ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Dispose()
    {
        if (!IsDisposed && _requireDispose)
        {
            if (_pointer != null) NativeMemory.Free(_pointer);

            _pointer = null;
        }

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    public Bitmap<T2> Convert<T2>()
        where T2 : unmanaged, IPixel<T2>
    {
        ThrowIfDisposed();
        var dst = new Bitmap<T2>(Width, Height);

        T* srcPtr = _pointer;
        T2* dstPtr = dst._pointer;

        Parallel.For(0, Width * Height, new ConvertOperation<T, T2>(srcPtr, dstPtr).Invoke);

        return dst;
    }

    public Bitmap<T> Clone()
    {
        ThrowIfDisposed();

        int size = ByteCount;
        var img = new Bitmap<T>(Width, Height);
        Buffer.MemoryCopy(_pointer, img._pointer, size, size);

        return img;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        DataSpan.Clear();
    }

    public void Fill(T fill)
    {
        ThrowIfDisposed();
        DataSpan.Fill(fill);
    }

    public void Flip(FlipMode mode)
    {
        ThrowIfDisposed();
        if (mode is FlipMode.Y or FlipMode.XY)
        {
            Parallel.For(0, Height, y => this[y].Reverse());
        }

        if (mode is FlipMode.X or FlipMode.XY)
        {
            Parallel.For(0, Height / 2, top =>
            {
                Span<T> tmp = stackalloc T[Width];
                int bottom = Height - top - 1;

                Span<T> topSpan = this[bottom];
                Span<T> bottomSpan = this[top];

                topSpan.CopyTo(tmp);
                bottomSpan.CopyTo(topSpan);
                tmp.CopyTo(bottomSpan);
            });
        }
    }

    public Bitmap<T> MakeBorder(int top, int bottom, int left, int right)
    {
        ThrowIfDisposed();

        int width = left + right + Width;
        int height = top + bottom + Height;
        var img = new Bitmap<T>(width, height);

        img[new PixelRect(left, top, Width, Height)] = this;

        return img;
    }

    public Bitmap<T> MakeBorder(int width, int height)
    {
        ThrowIfDisposed();

        int v = (height - Height) / 2;
        int h = (width - Width) / 2;

        return MakeBorder(v, v, h, h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowOutOfRange(int width, int height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "'width' is less than 0.");
        }
        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "'height' is less than 0.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowColOutOfRange(int x)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "'x' is less than 0.");
        }
        else if (x > Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"'x' is more than {Width}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowRowOutOfRange(int y)
    {
        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "'y' is less than 0.");
        }
        else if (y > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), $"'y' is more than {Height}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowOutOfRange(PixelRect roi)
    {
        if (roi.Bottom > Height) throw new ArgumentOutOfRangeException(nameof(roi));
        else if (roi.Right > Width) throw new ArgumentOutOfRangeException(nameof(roi));
    }

    IBitmap IBitmap.Clone() => Clone();
}

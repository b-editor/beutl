using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace Beutl.Media.Source;

// What an ImageSource opened. An SVG stays a drawing, which is sharp at any scale; density-aware paths draw or
// rasterize Picture at the required density, while Bitmap provides pixels at its declared size for pixel-only callers.
internal sealed class LoadedImage : IDisposable
{
    // A few bytes of SVG can declare any size. A drawing costs the same at any of them, but pixels do not.
    internal const int MaxSvgSide = 16384;
    internal const long MaxSvgPixels = 64L * 1024 * 1024;

    private readonly SKSvg? _svg;
    private readonly Lazy<Bitmap?> _bitmap;
    private readonly Uri? _uri;

    public LoadedImage(Bitmap bitmap, Uri? uri = null)
    {
        _uri = uri;
        _bitmap = new Lazy<Bitmap?>(bitmap);
        Size = new PixelSize(bitmap.Width, bitmap.Height);
    }

    private LoadedImage(SKSvg svg, SKPicture picture, PixelSize size)
    {
        _svg = svg;
        Picture = picture;
        Size = size;
        _bitmap = new Lazy<Bitmap?>(Rasterize);
    }

    // Null for an SVG too large to turn into pixels.
    public Bitmap? Bitmap => _bitmap.Value;

    public SKPicture? Picture { get; }

    public PixelSize Size { get; }

    public static LoadedImage Open(Uri uri)
    {
        using Stream stream = UriHelper.ResolveStream(uri);
        return IsSvg(uri) ? OpenSvg(stream) : new LoadedImage(Bitmap.FromStream(stream), uri);
    }

    // Decoding multiplies colors by alpha, which loses them where alpha is 0, so this reads the file again. Null for a
    // drawing, which has no colors under its alpha, and for a file that can no longer be read.
    public Bitmap? DecodeUnpremultiplied()
    {
        if (_uri == null)
            return null;

        try
        {
            using Stream stream = UriHelper.ResolveStream(_uri);
            using SKCodec? codec = SKCodec.Create(stream);
            if (codec == null)
                return null;

            // Without a color space, as SKBitmap.Decode reads the file for Bitmap, so both hold the same colors.
            SKImageInfo info = codec.Info.WithAlphaType(SKAlphaType.Unpremul).WithColorSpace(null);
            var pixels = new SKBitmap(info);
            if (codec.GetPixels(info, pixels.GetPixels()) is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                return Bitmap.FromFixedLayout(pixels);

            pixels.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_bitmap.IsValueCreated)
            _bitmap.Value?.Dispose();
        _svg?.Dispose();
    }

    private static bool IsSvg(Uri uri)
    {
        return uri.Scheme == "data"
            ? uri.OriginalString.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetExtension(uri.LocalPath), ".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static LoadedImage OpenSvg(Stream stream)
    {
        // A project and its media can come from anyone, and opening them must not read other files or contact another
        // host, as a file:// reference to a network share would. As in a browser's <img>, only what the SVG embeds draws.
        var parameters = new SvgParameters(null, null)
        {
            LoadOptions = new SvgDocumentLoadOptions
            {
                ProcessingMode = SvgProcessingMode.SecureStatic,
                ExternalResources = SvgExternalResourcePolicy.SameDocumentAndDataOnly,
            },
        };

        var svg = new SKSvg();
        try
        {
            SKPicture picture = svg.Load(stream, parameters)
                ?? throw new InvalidOperationException("Failed to read the SVG from the stream.");
            SKRect bounds = picture.CullRect;
            if (bounds.Width is not (> 0 and <= MaxSvgSide) || bounds.Height is not (> 0 and <= MaxSvgSide))
                throw new InvalidOperationException($"The SVG declares a size of {bounds.Width}x{bounds.Height}.");

            return new LoadedImage(svg, picture, new PixelSize((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height)));
        }
        catch
        {
            svg.Dispose();
            throw;
        }
    }

    // Computes the raster pixel size for an SVG at the requested density, bounded by both axis and total pixel budgets.
    public PixelSize ResolveSvgRasterSize(float density, int maxDimension)
    {
        if (Size.Width <= 0 || Size.Height <= 0)
            return Size;

        int maxSide = maxDimension > 0 ? Math.Min(maxDimension, MaxSvgSide) : MaxSvgSide;
        float sanitized = RenderScaleUtilities.SanitizeOutputScale(density);
        float clamped = BufferDimensionBudget.Named(maxSide).ClampWorkingScale(
            new Rect(0, 0, Size.Width, Size.Height),
            sanitized);

        int width = Math.Clamp((int)MathF.Ceiling(Size.Width * clamped), 1, maxSide);
        int height = Math.Clamp((int)MathF.Ceiling(Size.Height * clamped), 1, maxSide);

        if ((long)width * height > MaxSvgPixels)
        {
            double fit = Math.Sqrt((double)MaxSvgPixels / ((double)width * height));
            width = Math.Clamp((int)Math.Floor(width * fit), 1, maxSide);
            height = Math.Clamp((int)Math.Floor(height * fit), 1, maxSide);
        }

        return new PixelSize(width, height);
    }

    public Bitmap? RasterizePicture(
        PixelSize targetSize,
        BitmapColorType colorType,
        BitmapAlphaType alphaType,
        BitmapColorSpace colorSpace)
    {
        if (Picture == null
            || Size.Width <= 0
            || Size.Height <= 0
            || targetSize.Width <= 0
            || targetSize.Height <= 0
            || (long)targetSize.Width * targetSize.Height > MaxSvgPixels)
        {
            return null;
        }

        var bitmap = new Bitmap(targetSize.Width, targetSize.Height, colorType, alphaType, colorSpace);
        try
        {
            using var canvas = new SKCanvas(bitmap.SKBitmap);
            canvas.Clear(SKColors.Transparent);
            if (targetSize != Size)
            {
                canvas.Scale((float)targetSize.Width / Size.Width, (float)targetSize.Height / Size.Height);
            }

            canvas.DrawPicture(Picture);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private Bitmap? Rasterize()
        => RasterizePicture(Size, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
}

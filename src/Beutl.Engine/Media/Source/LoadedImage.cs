using Beutl.Serialization;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace Beutl.Media.Source;

// What an ImageSource opened. An SVG stays a drawing, which is sharp at any scale, and gets pixels at its own size
// only for what cannot take a drawing, such as an image brush.
internal sealed class LoadedImage : IDisposable
{
    // A few bytes of SVG can declare any size. A drawing costs the same at any of them, but pixels do not.
    private const int MaxSvgSide = 16384;
    private const long MaxSvgPixels = 64L * 1024 * 1024;

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
                return new Bitmap(pixels);

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

    private Bitmap? Rasterize()
    {
        if ((long)Size.Width * Size.Height > MaxSvgPixels)
            return null;

        var bitmap = new Bitmap(Size.Width, Size.Height, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        using var canvas = new SKCanvas(bitmap.SKBitmap);
        canvas.DrawPicture(Picture);
        return bitmap;
    }
}

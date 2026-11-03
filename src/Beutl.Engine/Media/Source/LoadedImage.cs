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

    public LoadedImage(Bitmap bitmap)
    {
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
        return IsSvg(uri) ? OpenSvg(stream) : new LoadedImage(Bitmap.FromStream(stream));
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

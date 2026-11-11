using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Editor.Services.LibraryThumbnails;

// The pictures the library's thumbnails show types on where the project has no content: a sample of each
// LibraryThumbnailSample for filter effects, and two clips for transitions. They were generated for Beutl with an
// image model. They are drawn in a 1920 x 1080 frame, the size most projects use, so a recipe's sizes look in a
// thumbnail as they would in a project.
internal static class LibraryThumbnailSamples
{
    public static readonly PixelSize FrameSize = new(1920, 1080);

    // The pictures are kept at two thirds of the size they cover in the frame: a whole frame is 1280 x 720 pixels,
    // four times a thumbnail's width, as captured content is at most.
    private const float PixelsPerUnit = 2f / 3;

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, ImageSource> s_sources = [];

    // What the samples are shown over: a dark ground the effect leaves alone, so what an effect adds around a cut-out,
    // such as a shadow or an outline, shows against it, and a transition that fades through nothing fades through it.
    public static Drawable CreateBackdrop(PixelSize frameSize)
    {
        return new RectShape
        {
            Width = { Value = frameSize.Width },
            Height = { Value = frameSize.Height },
            Fill = { Value = CreateVerticalGradient(Color.FromRgb(0x2E, 0x31, 0x3A), Color.FromRgb(0x15, 0x16, 0x1A)) },
        };
    }

    // The picture a filter effect applies to, centred, in a group the effect goes on, so a recipe can move it as a
    // whole and the effect sees one picture.
    public static DrawableGroup CreateEffectSubject(LibraryThumbnailSample sample)
    {
        var subject = new DrawableGroup();
        subject.Children.Add(CreateImage(GetFileName(sample)));
        return subject;
    }

    // One side of a transition: a whole frame of colors of its own, so where each side is and which way a transition
    // moves read at a glance.
    public static Drawable CreateClip(bool incoming)
    {
        return CreateImage(incoming ? "transition-b.webp" : "transition-a.webp");
    }

    // The source of a sample's picture, for tests.
    internal static ImageSource GetSource(LibraryThumbnailSample sample) => GetSource(GetFileName(sample));

    // The source of a transition's clip, for tests.
    internal static ImageSource GetClipSource(bool incoming)
    {
        return GetSource(incoming ? "transition-b.webp" : "transition-a.webp");
    }

    private static string GetFileName(LibraryThumbnailSample sample)
    {
        return sample switch
        {
            LibraryThumbnailSample.NightLights => "night.webp",
            LibraryThumbnailSample.GreenScreen => "greenscreen.webp",
            LibraryThumbnailSample.Object => "object.webp",
            LibraryThumbnailSample.Text => "text.webp",
            LibraryThumbnailSample.Shapes => "shapes.webp",
            _ => "landscape.webp",
        };
    }

    // A picture at the size it covers in the frame, centred. Its source comes through an expression, which does not
    // make it a child of the scene, so every thumbnail shares it and one decoded copy of it while any draws it.
    private static SourceImage CreateImage(string fileName)
    {
        return new SourceImage
        {
            Source = { Expression = new SharedValueExpression<ImageSource?>(GetSource(fileName)) },
            Transform =
            {
                Value = new ScaleTransform
                {
                    ScaleX = { Value = 100 / PixelsPerUnit },
                    ScaleY = { Value = 100 / PixelsPerUnit },
                }
            },
        };
    }

    // The picture is read into memory once, and thumbnails decode it from there, as captured content is.
    private static ImageSource GetSource(string fileName)
    {
        lock (s_lock)
        {
            if (!s_sources.TryGetValue(fileName, out ImageSource? source))
            {
                using Stream stream = typeof(LibraryThumbnailSamples).Assembly.GetManifestResourceStream(
                        $"LibraryThumbnailSamples/{fileName}")
                    ?? throw new InvalidOperationException($"The sample picture {fileName} is not in the assembly.");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                source = ImageSource.Open(UriHelper.CreateBase64DataUri("image/webp", bytes.ToArray()));
                s_sources.Add(fileName, source);
            }

            return source;
        }
    }

    private static LinearGradientBrush CreateVerticalGradient(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0.5f, 0, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(0.5f, 1, RelativeUnit.Relative) },
        };
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(bottom, 1));
        return brush;
    }
}

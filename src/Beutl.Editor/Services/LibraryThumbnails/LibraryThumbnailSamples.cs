using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Editor.Services.LibraryThumbnails;

// The pictures the library's thumbnails are made of. They are drawn in a 1920 x 1080 frame, the size most projects
// use, so a recipe's sizes look in a thumbnail as they would in a project.
internal static class LibraryThumbnailSamples
{
    public static readonly PixelSize FrameSize = new(1920, 1080);

    // Half the size of the card the effect sample draws, which recipes position other things against.
    public const float CardHalfWidth = 600;
    public const float CardHalfHeight = 340;

    // The green of the effect sample's ground, close to a green screen's, which the key effects remove.
    private static readonly Color GroundColor = Color.FromRgb(0x22, 0xC5, 0x5E);

    private static readonly Point SunOffset = new(300, -110);

    private static readonly (float X, float Y, float Size)[] s_highlights =
        [(-470, -230, 22), (-360, -270, 14), (-120, -250, 18), (40, -200, 12)];

    // What the samples are shown over: a dark ground the effect leaves alone, so what an effect adds around the
    // sample, such as a shadow or an outline, shows against it, and a transition that fades through nothing fades
    // through it.
    public static Drawable CreateBackdrop(PixelSize frameSize)
    {
        return new RectShape
        {
            Width = { Value = frameSize.Width },
            Height = { Value = frameSize.Height },
            Fill = { Value = CreateVerticalGradient(Color.FromRgb(0x2E, 0x31, 0x3A), Color.FromRgb(0x15, 0x16, 0x1A)) },
        };
    }

    // The picture a filter effect applies to: a card of smooth gradients with a bright sun, a green ground, small
    // highlights and white text, in a transparent margin. Color, blur, key, edge and shadow effects all find
    // something there to change.
    //
    // The effect goes on the outer group, whose one child is the picture flattened by a layer effect, so the effect
    // sees one picture as it would on an image. On the shapes themselves an effect that works per target, such as a
    // split, would treat each shape on its own.
    public static DrawableGroup CreateEffectSubject()
    {
        var subject = new DrawableGroup();
        subject.Children.Add(CreatePicture());
        return subject;
    }

    private static DrawableGroup CreatePicture()
    {
        var group = new DrawableGroup { FilterEffect = { Value = new LayerEffect() } };
        group.Children.Add(new RoundedRectShape
        {
            Width = { Value = CardHalfWidth * 2 },
            Height = { Value = CardHalfHeight * 2 },
            CornerRadius = { Value = new CornerRadius(56) },
            Fill =
            {
                Value = CreateVerticalGradient(
                    (Color.FromRgb(0x25, 0x63, 0xEB), 0f),
                    (Color.FromRgb(0x7C, 0x3A, 0xED), 0.55f),
                    (Color.FromRgb(0xFB, 0x92, 0x3C), 1f))
            },
        });
        group.Children.Add(new RoundedRectShape
        {
            Width = { Value = CardHalfWidth * 2 },
            Height = { Value = 180 },
            CornerRadius = { Value = new CornerRadius(0, 0, 56, 56) },
            Fill = { Value = new SolidColorBrush(GroundColor) },
            Transform = { Value = new TranslateTransform(0, CardHalfHeight - 90) },
        });
        group.Children.Add(new EllipseShape
        {
            Width = { Value = 230 },
            Height = { Value = 230 },
            Fill =
            {
                Value = new RadialGradientBrush
                {
                    Radius = { Value = 50 },
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xFF, 0xFB, 0xEB), 0f),
                        new GradientStop(Color.FromRgb(0xFA, 0xCC, 0x15), 1f),
                    },
                }
            },
            Transform = { Value = new TranslateTransform(SunOffset.X, SunOffset.Y) },
        });
        foreach ((float x, float y, float size) in s_highlights)
        {
            group.Children.Add(new EllipseShape
            {
                Width = { Value = size },
                Height = { Value = size },
                Fill = { Value = new SolidColorBrush(Colors.White) },
                Transform = { Value = new TranslateTransform(x, y) },
            });
        }

        group.Children.Add(new TextBlock
        {
            Text = { Value = "Beutl" },
            Size = { Value = 210 },
            FontWeight = { Value = FontWeight.Bold },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            Transform = { Value = new TranslateTransform(-170, 10) },
        });
        return group;
    }

    // One side of a transition: a full frame with a color and a letter of its own, so where each side is and which
    // way a transition moves read at a glance.
    public static Drawable CreateClip(bool incoming)
    {
        var group = new DrawableGroup();
        group.Children.Add(new RectShape
        {
            Width = { Value = FrameSize.Width },
            Height = { Value = FrameSize.Height },
            Fill =
            {
                Value = incoming
                    ? CreateVerticalGradient(Color.FromRgb(0xFD, 0xBA, 0x74), Color.FromRgb(0xDB, 0x27, 0x77))
                    : CreateVerticalGradient(Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x1E, 0x3A, 0x8A))
            },
        });
        group.Children.Add(new EllipseShape
        {
            Width = { Value = 380 },
            Height = { Value = 380 },
            Fill = { Value = new SolidColorBrush(incoming ? Color.FromRgb(0xFE, 0xF0, 0x8A) : Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)) },
            Transform = { Value = new TranslateTransform(incoming ? -560 : 560, -230) },
        });
        group.Children.Add(new TextBlock
        {
            Text = { Value = incoming ? "B" : "A" },
            Size = { Value = 560 },
            FontWeight = { Value = FontWeight.Bold },
            Fill = { Value = new SolidColorBrush(Colors.White) },
        });
        return group;
    }

    private static LinearGradientBrush CreateVerticalGradient(Color top, Color bottom)
    {
        return CreateVerticalGradient((top, 0f), (bottom, 1f));
    }

    private static LinearGradientBrush CreateVerticalGradient(params ReadOnlySpan<(Color Color, float Offset)> stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0.5f, 0, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(0.5f, 1, RelativeUnit.Relative) },
        };
        foreach ((Color color, float offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }
}

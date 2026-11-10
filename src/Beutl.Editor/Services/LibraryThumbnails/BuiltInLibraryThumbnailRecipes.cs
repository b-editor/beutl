using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Editor.Services.LibraryThumbnails;

// The recipes of the built-in filter effects. Each moves the value that shows what the effect does, from none at
// the start of the thumbnail to the most at its middle, the frame shown at rest. Sizes are given for a full HD frame
// and scaled to what the effect applies to. An effect left out shows its own
// look at its defaults, such as a shake, or needs what the samples do not have, such as a LUT file, a script or
// another effect to hold.
internal static class BuiltInLibraryThumbnailRecipes
{
    public static void Register()
    {
        RegisterBlurs();
        RegisterShadows();
        RegisterShapes();
        RegisterColors();
        RegisterKeys();
        RegisterDistortions();
    }

    private static void RegisterBlurs()
    {
        LibraryThumbnailRecipes.Register<Blur>((effect, context) =>
            context.Animate(effect.Sigma, p => Square(context.Scale(36) * Wave(p))));

        // A thumbnail is a sixth of the frame, so the radius is wide enough to leave a halo there.
        LibraryThumbnailRecipes.Register<Sharpen>((effect, context) =>
        {
            effect.Radius.Value = context.Scale(24);
            context.Animate(effect.Amount, p => 500 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<RadialBlur>((effect, context) =>
            context.Animate(effect.Amount, p => 40 * Wave(p)));

        // A motion blur only shows on moving content, so the sample sweeps from side to side, fastest at the middle.
        LibraryThumbnailRecipes.Register<MotionBlur>((effect, context) =>
        {
            effect.ShutterAngle.Value = 360;
            effect.Samples.Value = 24;
            var move = new TranslateTransform();
            context.Animate(move.X, p => context.Scale(480) * MathF.Sin(p * MathF.Tau));
            context.Subject?.Transform.Value = move;
        });

        LibraryThumbnailRecipes.Register<Bokeh>((effect, context) =>
        {
            effect.Highlight.Value = 1;
            context.Animate(effect.Radius, p => context.Scale(32) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<Bloom>((effect, context) =>
        {
            effect.Radius.Value = context.Scale(48);
            effect.Threshold.Value = 0.45f;
            context.Animate(effect.Intensity, p => 3 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<LightRays>((effect, context) =>
        {
            effect.Intensity.Value = 1.5f;
            context.Animate(effect.Length, p => 90 * Wave(p));
        });
    }

    private static void RegisterShadows()
    {
        LibraryThumbnailRecipes.Register<DropShadow>((effect, context) =>
        {
            effect.Color.Value = Color.FromArgb(0xD0, 0x00, 0x00, 0x00);
            context.Animate(effect.Position, p => new Point(context.Scale(48) * Wave(p), context.Scale(48) * Wave(p)));
            context.Animate(effect.Sigma, p => Square(context.Scale(28) * Wave(p)));
        });

        LibraryThumbnailRecipes.Register<InnerShadow>((effect, context) =>
        {
            effect.Color.Value = Color.FromArgb(0xE0, 0x00, 0x00, 0x00);
            effect.Sigma.Value = Square(context.Scale(18));
            context.Animate(effect.Position, p => new Point(context.Scale(40) * Wave(p), context.Scale(40) * Wave(p)));
        });

        LibraryThumbnailRecipes.Register<FlatShadow>((effect, context) =>
        {
            effect.Angle.Value = 45;
            effect.Brush.Value = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
            context.Animate(effect.Length, p => context.Scale(180) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<StrokeEffect>((effect, context) =>
        {
            var pen = new Pen
            {
                Brush = { Value = new SolidColorBrush(Colors.White) },
                StrokeAlignment = { Value = StrokeAlignment.Outside },
                StrokeJoin = { Value = StrokeJoin.Round },
            };
            context.Animate(pen.Thickness, p => context.Scale(4 + (24 * Wave(p))));
            effect.Pen.Value = pen;
        });
    }

    private static void RegisterShapes()
    {
        LibraryThumbnailRecipes.Register<Clipping>((effect, context) =>
        {
            context.Animate(effect.Left, p => context.Scale(300) * Wave(p));
            context.Animate(effect.Right, p => context.Scale(300) * Wave(p));
            context.Animate(effect.Top, p => context.Scale(150) * Wave(p));
            context.Animate(effect.Bottom, p => context.Scale(150) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<DiagonalClipping>((effect, context) =>
            context.Animate(effect.Width, p => context.Scale(2400 - (2000 * Wave(p)))));

        LibraryThumbnailRecipes.Register<Dilate>((effect, context) =>
        {
            context.Animate(effect.RadiusX, p => context.Scale(20) * Wave(p));
            context.Animate(effect.RadiusY, p => context.Scale(20) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<Erode>((effect, context) =>
        {
            context.Animate(effect.RadiusX, p => context.Scale(20) * Wave(p));
            context.Animate(effect.RadiusY, p => context.Scale(20) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<SplitEffect>((effect, context) =>
        {
            effect.HorizontalDivisions.Value = 3;
            context.Animate(effect.HorizontalSpacing, p => context.Scale(80) * Wave(p));
            context.Animate(effect.VerticalSpacing, p => context.Scale(80) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<RepeatEffect>((effect, context) =>
            context.Animate(effect.Spacing, p => Square(context.Scale(120) * Wave(p))));

        LibraryThumbnailRecipes.Register<TransformEffect>((effect, context) =>
        {
            var rotation = new RotationTransform();
            context.Animate(rotation.Rotation, p => 30 * Wave(p));
            effect.Transform.Value = rotation;
        });

        LibraryThumbnailRecipes.Register<MosaicEffect>((effect, context) =>
            context.Animate(effect.TileSize, p => Square(context.Scale(8 + (72 * Wave(p))))));
    }

    private static void RegisterColors()
    {
        LibraryThumbnailRecipes.Register<HueRotate>((effect, context) =>
            context.Animate(effect.Angle, p => 360 * p));

        LibraryThumbnailRecipes.Register<Saturate>((effect, context) =>
            context.Animate(effect.Amount, p => 100 - (100 * Wave(p))));

        LibraryThumbnailRecipes.Register<Brightness>((effect, context) =>
            context.Animate(effect.Amount, p => 100 + (120 * Wave(p))));

        LibraryThumbnailRecipes.Register<Gamma>((effect, context) =>
            context.Animate(effect.Amount, p => 100 + (150 * Wave(p))));

        LibraryThumbnailRecipes.Register<Invert>((effect, context) =>
            context.Animate(effect.Amount, p => 100 * Wave(p)));

        LibraryThumbnailRecipes.Register<Negaposi>((effect, context) =>
        {
            effect.Red.Value = 255;
            effect.Green.Value = 255;
            effect.Blue.Value = 255;
            context.Animate(effect.Strength, p => 100 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<Threshold>((effect, context) =>
            context.Animate(effect.Value, p => 25 + (50 * Wave(p))));

        LibraryThumbnailRecipes.Register<HighContrast>((effect, context) =>
            context.Animate(effect.Contrast, p => 90 * Wave(p)));

        LibraryThumbnailRecipes.Register<Lighting>((effect, context) =>
        {
            context.Animate(effect.Multiply, p => Mix(Colors.White, Color.FromRgb(0xFF, 0xA8, 0x70), Wave(p)));
            context.Animate(effect.Add, p => Mix(Colors.Black, Color.FromRgb(0x30, 0x10, 0x00), Wave(p)));
        });

        LibraryThumbnailRecipes.Register<ColorGrading>((effect, context) =>
        {
            context.Animate(effect.Temperature, p => 70 * Wave(p));
            context.Animate(effect.Contrast, p => 30 * Wave(p));
            context.Animate(effect.Saturation, p => 25 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<Curves>((effect, context) =>
            context.Animate(effect.MasterCurve, p =>
            {
                float bend = 0.18f * Wave(p);
                return new CurveMap(
                [
                    new CurveControlPoint(0, 0),
                    new CurveControlPoint(0.25f, 0.25f - bend),
                    new CurveControlPoint(0.75f, 0.75f + bend),
                    new CurveControlPoint(1, 1),
                ]);
            }));

        LibraryThumbnailRecipes.Register<ColorShift>((effect, context) =>
        {
            context.Animate(effect.RedOffset, p => new PixelPoint((int)MathF.Round(context.Scale(36) * Wave(p)), 0));
            context.Animate(effect.BlueOffset, p => new PixelPoint(-(int)MathF.Round(context.Scale(36) * Wave(p)), 0));
        });

        LibraryThumbnailRecipes.Register<BlendEffect>((effect, context) =>
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = { Value = RelativePoint.TopLeft },
                EndPoint = { Value = RelativePoint.BottomRight },
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xF4, 0x3F, 0x5E), 0),
                    new GradientStop(Color.FromRgb(0x06, 0xB6, 0xD4), 1),
                },
            };
            // Unlike the default, which keeps only the brush, this keeps the picture where the brush is clear, so
            // the thumbnail starts from the picture.
            effect.BlendMode.Value = BlendMode.SrcATop;
            context.Animate(brush.Opacity, p => 70 * Wave(p));
            effect.Brush.Value = brush;
        });
    }

    private static void RegisterKeys()
    {
        LibraryThumbnailRecipes.Register<ChromaKey>((effect, context) =>
        {
            effect.Color.Value = GreenScreen;
            context.Animate(effect.HueRange, p => 40 * Wave(p));
            context.Animate(effect.SaturationRange, p => 60 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<ColorKey>((effect, context) =>
        {
            effect.Color.Value = GreenScreen;
            context.Animate(effect.Range, p => 12 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<LumaKey>((effect, context) =>
        {
            effect.Softness.Value = 10;
            context.Animate(effect.Lower, p => 40 * Wave(p));
        });
    }

    private static void RegisterDistortions()
    {
        LibraryThumbnailRecipes.Register<WaveRefraction>((effect, context) =>
        {
            effect.Amount.Value = context.Scale(12);
            effect.Wavelength.Value = context.Scale(180);
            context.Animate(effect.Phase, p => 360 * p);
        });

        LibraryThumbnailRecipes.Register<PixelSortEffect>((effect, context) =>
        {
            effect.ThresholdMin.Value = 10;
            context.Animate(effect.ThresholdMax, p => 20 + (75 * Wave(p)));
        });

        // The map pushes the middle of the picture aside, most where the map is most opaque.
        LibraryThumbnailRecipes.Register<DisplacementMapEffect>((effect, context) =>
        {
            effect.DisplacementMap.Value = new RadialGradientBrush
            {
                Radius = { Value = 50 },
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1),
                },
            };
            var translate = new DisplacementMapTranslateTransform();
            context.Animate(translate.X, p => context.Scale(160) * Wave(p));
            effect.Transform.Value = translate;
        });

        // The picture travels once around an ellipse.
        LibraryThumbnailRecipes.Register<PathFollowEffect>((effect, context) =>
        {
            effect.Geometry.Value = new EllipseGeometry
            {
                Width = { Value = context.Scale(520) },
                Height = { Value = context.Scale(240) },
            };
            context.Animate(effect.Progress, p => 100 * p);
        });

        LibraryThumbnailRecipes.Register<EdgeDetection>((effect, context) =>
            context.Animate(effect.Thickness, p => context.Scale(1 + (3 * Wave(p)))));
    }

    // The green of a green screen, which the key effects remove; the sample's ground is close to it.
    private static readonly Color GreenScreen = Color.FromRgb(0x00, 0xB1, 0x40);

    private static float Wave(float progress) => LibraryThumbnailContext.Wave(progress);

    private static Size Square(float value) => new(value, value);

    private static Color Mix(Color from, Color to, float amount)
    {
        static byte Lerp(byte a, byte b, float t) => (byte)Math.Clamp(MathF.Round(a + ((b - a) * t)), 0, 255);

        return Color.FromArgb(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }
}

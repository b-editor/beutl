using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Editor.Services.LibraryThumbnails;

// The recipes of the built-in filter effects. Each moves the value that shows what the effect does, from none at
// the start of the thumbnail to the most at its middle, the frame shown at rest, and picks the sample picture that
// shows the effect best where the project has no content. Sizes are given for a full HD frame and scaled to what the
// effect applies to. An effect left out needs what the samples do not have, such as a LUT file, a script or another
// effect to hold, and is shown at its defaults on the landscape.
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

        // A thumbnail is a sixth of the frame, so the radius is wide enough to leave a halo there, and the amount no
        // more than a photograph takes before its edges glow.
        LibraryThumbnailRecipes.Register<Sharpen>((effect, context) =>
        {
            effect.Radius.Value = context.Scale(24);
            context.Animate(effect.Amount, p => 250 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<RadialBlur>((effect, context) =>
            context.Animate(effect.Amount, p => 40 * Wave(p)));

        // A motion blur only shows on moving content, so the picture sweeps from side to side, fastest at the middle.
        LibraryThumbnailRecipes.Register<MotionBlur>(LibraryThumbnailSample.Object, (effect, context) =>
        {
            effect.ShutterAngle.Value = 360;
            effect.Samples.Value = 24;
            var move = new TranslateTransform();
            context.Animate(move.X, p => context.Scale(480) * MathF.Sin(p * MathF.Tau));
            context.Subject?.Transform.Value = move;
        });

        LibraryThumbnailRecipes.Register<Bokeh>(LibraryThumbnailSample.NightLights, (effect, context) =>
        {
            effect.Highlight.Value = 1;
            context.Animate(effect.Radius, p => context.Scale(32) * Wave(p));
        });

        // The lights of a night picture are small in a thumbnail, so they glow wide.
        LibraryThumbnailRecipes.Register<Bloom>(LibraryThumbnailSample.NightLights, (effect, context) =>
        {
            effect.Radius.Value = context.Scale(96);
            effect.Threshold.Value = 0.3f;
            context.Animate(effect.Intensity, p => 4 * Wave(p));
        });

        // Rays need bright shapes against dark around them, as letters are.
        LibraryThumbnailRecipes.Register<LightRays>(LibraryThumbnailSample.Text, (effect, context) =>
        {
            effect.Intensity.Value = 1.5f;
            context.Animate(effect.Length, p => 90 * Wave(p));
        });
    }

    private static void RegisterShadows()
    {
        LibraryThumbnailRecipes.Register<DropShadow>(LibraryThumbnailSample.Object, (effect, context) =>
        {
            effect.Color.Value = Color.FromArgb(0xD0, 0x00, 0x00, 0x00);
            context.Animate(effect.Position, p => new Point(context.Scale(48) * Wave(p), context.Scale(48) * Wave(p)));
            context.Animate(effect.Sigma, p => Square(context.Scale(28) * Wave(p)));
        });

        LibraryThumbnailRecipes.Register<InnerShadow>(LibraryThumbnailSample.Text, (effect, context) =>
        {
            effect.Color.Value = Color.FromArgb(0xE0, 0x00, 0x00, 0x00);
            effect.Sigma.Value = Square(context.Scale(18));
            context.Animate(effect.Position, p => new Point(context.Scale(40) * Wave(p), context.Scale(40) * Wave(p)));
        });

        LibraryThumbnailRecipes.Register<FlatShadow>(LibraryThumbnailSample.Text, (effect, context) =>
        {
            effect.Angle.Value = 45;
            effect.Brush.Value = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
            context.Animate(effect.Length, p => context.Scale(180) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<StrokeEffect>(LibraryThumbnailSample.Text, (effect, context) =>
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

        LibraryThumbnailRecipes.Register<Dilate>(LibraryThumbnailSample.Text, (effect, context) =>
        {
            context.Animate(effect.RadiusX, p => context.Scale(20) * Wave(p));
            context.Animate(effect.RadiusY, p => context.Scale(20) * Wave(p));
        });

        LibraryThumbnailRecipes.Register<Erode>(LibraryThumbnailSample.Text, (effect, context) =>
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

        LibraryThumbnailRecipes.Register<RepeatEffect>(LibraryThumbnailSample.Object, (effect, context) =>
            context.Animate(effect.Spacing, p => Square(context.Scale(120) * Wave(p))));

        LibraryThumbnailRecipes.Register<TransformEffect>(LibraryThumbnailSample.Object, (effect, context) =>
        {
            var rotation = new RotationTransform();
            context.Animate(rotation.Rotation, p => 30 * Wave(p));
            effect.Transform.Value = rotation;
        });

        // A shake shows at its defaults. An object shakes with room around it, where a whole frame would show its
        // edges.
        LibraryThumbnailRecipes.Register<ShakeEffect>(LibraryThumbnailSample.Object, (_, _) => { });

        LibraryThumbnailRecipes.Register<MosaicEffect>((effect, context) =>
            context.Animate(effect.TileSize, p => Square(context.Scale(8 + (72 * Wave(p))))));
    }

    private static void RegisterColors()
    {
        LibraryThumbnailRecipes.Register<HueRotate>(LibraryThumbnailSample.Shapes, (effect, context) =>
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

        // The value is of linear light, where a quarter leaves the brighter part of a picture.
        LibraryThumbnailRecipes.Register<Threshold>((effect, context) =>
            context.Animate(effect.Value, p => 10 + (15 * Wave(p))));

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

        LibraryThumbnailRecipes.Register<ColorShift>(LibraryThumbnailSample.Shapes, (effect, context) =>
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
        LibraryThumbnailRecipes.Register<ChromaKey>(LibraryThumbnailSample.GreenScreen, (effect, context) =>
        {
            effect.Color.Value = ScreenKey;
            context.Animate(effect.HueRange, p => 40 * Wave(p));
            context.Animate(effect.SaturationRange, p => 60 * Wave(p));
        });

        // A color key removes what is as bright as its color. The yellow of the shapes is as bright as nothing else
        // there, so it goes alone, where a green screen would take a subject as bright as the green with it. The key
        // is a little brighter than the yellow, which a key at no range would otherwise remove in part.
        LibraryThumbnailRecipes.Register<ColorKey>(LibraryThumbnailSample.Shapes, (effect, context) =>
        {
            effect.Color.Value = Color.FromRgb(0xFF, 0xD7, 0x28);
            context.Animate(effect.Range, p => 12 * Wave(p));
        });

        LibraryThumbnailRecipes.Register<LumaKey>(LibraryThumbnailSample.NightLights, (effect, context) =>
        {
            effect.Softness.Value = 10;
            context.Animate(effect.Lower, p => 40 * Wave(p));
        });
    }

    private static void RegisterDistortions()
    {
        LibraryThumbnailRecipes.Register<WaveRefraction>(LibraryThumbnailSample.Shapes, (effect, context) =>
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
        LibraryThumbnailRecipes.Register<DisplacementMapEffect>(LibraryThumbnailSample.Shapes, (effect, context) =>
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
        LibraryThumbnailRecipes.Register<PathFollowEffect>(LibraryThumbnailSample.Object, (effect, context) =>
        {
            effect.Geometry.Value = new EllipseGeometry
            {
                Width = { Value = context.Scale(520) },
                Height = { Value = context.Scale(240) },
            };
            context.Animate(effect.Progress, p => 100 * p);
        });

        LibraryThumbnailRecipes.Register<EdgeDetection>(LibraryThumbnailSample.Shapes, (effect, context) =>
            context.Animate(effect.Thickness, p => context.Scale(1 + (3 * Wave(p)))));
    }

    // A green screen's green, which a chroma key removes. The green screen sample's backdrop is #01B03C, 11 degrees of
    // hue from this: a key at no range removes what is within a degree or so of its color, which would take the
    // backdrop in part from the first frame, and a range that widens takes it whole.
    private static readonly Color ScreenKey = Color.FromRgb(0x00, 0xB1, 0x5C);

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

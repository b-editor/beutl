using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class PolarCoordinatesTests
{
    private const int Width = 129;
    private const int Height = 81;
    private static readonly PixelSize s_size = new(Width, Height);
    private static readonly Rect s_bounds = new(10.25f, -20.5f, 100, 60);

    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 0f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 0f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, -20f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.NaN)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.PositiveInfinity)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.NegativeInfinity)]
    public void ZeroOrInvalidAmountLeavesFinitePixelsUnchanged(PolarCoordinatesDirection direction, float amount)
    {
        // Set the resource directly to exercise values that can bypass property validation.
        using var resource = new PolarCoordinates.Resource { Direction = direction, Amount = amount };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(after.All(float.IsFinite), Is.True);
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public void RectangularToPolarWrapsRowsIntoAnInscribedEllipse()
    {
        using PolarCoordinates.Resource resource = new PolarCoordinates().ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, canvas =>
        {
            canvas.Clear(SKColors.Lime);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(0, 0, Width, 6), paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(new SKRect(0, Height - 6, Width, Height), paint);
        });

        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, Width / 2, Height / 2, 0), Is.GreaterThan(0.99f), "top row at the center");
            foreach ((int x, int y) in new[] { (Width / 2, 0), (0, Height / 2), (Width - 1, Height / 2), (Width / 2, Height - 1) })
                Assert.That(Channel(pixels, x, y, 2), Is.GreaterThan(0.99f), $"bottom row at the rim ({x}, {y})");
            foreach ((int x, int y) in new[] { (0, 0), (Width - 1, 0), (0, Height - 1), (Width - 1, Height - 1) })
                Assert.That(Channel(pixels, x, y, 3), Is.LessThan(0.001f), $"transparent corner ({x}, {y})");
        });
    }

    [Test]
    public void AmountBlendsTheSamplingCoordinates()
    {
        var effect = new PolarCoordinates { Amount = { Value = 50f } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // The center's mapped sample is the first texel, so 50% samples halfway to it.
        float expected = Channel(before, Width / 4, Height / 4, 0);
        Assert.That(Channel(after, Width / 2, Height / 2, 0), Is.EqualTo(expected).Within(0.005f));
    }

    [Test]
    public void PolarToRectangularRecoversTheInterior()
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                new PolarCoordinates(),
                new PolarCoordinates { Direction = { Value = PolarCoordinatesDirection.PolarToRectangular } },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // Near the collapsed center and the seam, multiple source pixels can share an output texel.
        float maximumError = 0f;
        for (int y = Height / 4; y < Height - 10; y++)
        {
            for (int x = 12; x < Width - 12; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                    maximumError = MathF.Max(maximumError, MathF.Abs(Channel(after, x, y, channel) - Channel(before, x, y, channel)));
            }
        }

        Assert.That(maximumError, Is.LessThan(0.04f));
    }

    [TestCase(PolarCoordinatesDirection.RectangularToPolar)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular)]
    public void KeepsInputBoundsAndRequiresTheFullSource(PolarCoordinatesDirection direction)
    {
        var effect = new PolarCoordinates { Direction = { Value = direction } };
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(s_bounds));
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description.Bounds.RequiresFullInput, Is.True);
            Assert.That(description.SourceTileMode, Is.EqualTo(SKShaderTileMode.Decal));
        });
    }

    [Test]
    public void TwelveOClockSeamIsOpaque()
    {
        (_, float[] pixels) = FilterEffectTestHarness.RunOnSolidTarget(new PolarCoordinates(), SKColors.White, s_size);

        for (int y = 4; y < Height / 2 - 4; y++)
        {
            for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++)
                Assert.That(Channel(pixels, x, y, 3), Is.GreaterThan(0.99f), $"seam pixel ({x}, {y})");
        }
    }

    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 100f, 75f, 25f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 50f, 75f, 50f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 100f, 25f, 75f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 50f, 25f, 50f)]
    public void HitTestUsesTheMappedSourcePoint(PolarCoordinatesDirection direction, float amount, float outputX, float sourceX)
    {
        var effect = new PolarCoordinates { Direction = { Value = direction }, Amount = { Value = amount } };
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);
        RenderHitTestContract hitTest = description.HitTest!.Value;
        var accepted = new Rect(s_bounds.X + sourceX - 1, s_bounds.Center.Y - 1, 2, 2);
        var input = new RenderHitTestInput(s_bounds, accepted.ContainsExclusive);
        var point = new Point(s_bounds.X + outputX, s_bounds.Center.Y);

        Assert.Multiple(() =>
        {
            Assert.That(hitTest.Evaluate(output, [input], [], point), Is.True);
            Assert.That(hitTest.Evaluate(output, [input], [], point + new Vector(0, 10)), Is.False);
            Assert.That(hitTest.Evaluate(output, [new RenderHitTestInput(s_bounds, _ => true)], [], s_bounds.BottomRight), Is.False);
        });
    }

    [TestCase(90f)]
    [TestCase(450f)]
    [TestCase(-270f)]
    public void AngleRotatesRectangularToPolarClockwise(float angle)
    {
        var effect = new PolarCoordinates { Angle = { Value = angle } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawQuadrantStripes);

        // With a +90 degree rotation, the four source columns (R, G, B, W) shift clockwise by one quadrant:
        // R: 3..6 o'clock, G: 6..9 o'clock, B: 9..12 o'clock, W: 12..3 o'clock.
        int rx = Width / 2 + Width / 6;
        int ry = Height / 2 + Height / 6;
        int gx = Width / 2 - Width / 6;
        int gy = Height / 2 + Height / 6;
        int bx = Width / 2 - Width / 6;
        int by = Height / 2 - Height / 6;
        int wx = Width / 2 + Width / 6;
        int wy = Height / 2 - Height / 6;

        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, rx, ry, 0), Is.GreaterThan(0.95f), "3..6 o'clock red");
            Assert.That(Channel(pixels, rx, ry, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, gx, gy, 1), Is.GreaterThan(0.95f), "6..9 o'clock green");
            Assert.That(Channel(pixels, gx, gy, 0), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, bx, by, 2), Is.GreaterThan(0.95f), "9..12 o'clock blue");
            Assert.That(Channel(pixels, bx, by, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, wx, wy, 0), Is.GreaterThan(0.95f), "12..3 o'clock white");
            Assert.That(Channel(pixels, wx, wy, 1), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, wx, wy, 2), Is.GreaterThan(0.95f));
        });
    }

    [Test]
    public void CenterMovesThePoleInBothRelativeAndAbsoluteUnits()
    {
        var relative = new PolarCoordinates { Center = { Value = new RelativePoint(0.25f, 0.25f, RelativeUnit.Relative) } };
        var absolute = new PolarCoordinates
        {
            Center = { Value = new RelativePoint(Width * 0.25f, Height * 0.25f, RelativeUnit.Absolute) },
        };
        var clipped = new PolarCoordinates
        {
            Center = { Value = new RelativePoint(0.25f, 0.25f, RelativeUnit.Relative) },
            ClipToBounds = { Value = true },
        };
        using PolarCoordinates.Resource relativeResource = relative.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource absoluteResource = absolute.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource clippedResource = clipped.ToResource(CompositionContext.Default);

        (float[] relativePixels, Rect relativeBounds, PixelSize relativeSize) = RunWithBounds(relativeResource, s_size, DrawTopBottomBands);
        (float[] absolutePixels, Rect absoluteBounds, _) = RunWithBounds(absoluteResource, s_size, DrawTopBottomBands);
        (_, float[] clippedPixels) = FilterEffectTestHarness.RunOnTarget(clippedResource, s_size, DrawTopBottomBands);

        int cx = Width / 4;
        int cy = Height / 4;
        Assert.Multiple(() =>
        {
            Assert.That(relativeBounds.X, Is.EqualTo(-Width * 0.25f).Within(0.01f), "bounds follow shifted center X");
            Assert.That(relativeBounds.Y, Is.EqualTo(-Height * 0.25f).Within(0.01f), "bounds follow shifted center Y");
            Assert.That(relativeBounds.Size, Is.EqualTo(new Size(Width, Height)));
            Assert.That(absoluteBounds, Is.EqualTo(relativeBounds));
            Assert.That(Channel(relativePixels, relativeSize.Width, relativeSize.Width / 2, relativeSize.Height / 2, 0), Is.GreaterThan(0.95f), "top row maps to shifted center inside resized bounds");
            Assert.That(Channel(relativePixels, relativeSize.Width, relativeSize.Width / 2, 2, 2), Is.GreaterThan(0.95f), "top rim is preserved without clipping");
            Assert.That(absolutePixels, Is.EqualTo(relativePixels).Within(0.001f));
            Assert.That(Channel(clippedPixels, cx, cy, 0), Is.GreaterThan(0.95f), "clipped mode keeps pole at shifted local point");
            Assert.That(Channel(clippedPixels, Width - 2, Height - 2, 3), Is.LessThan(0.001f), "clipped far corner past rim is transparent");
        });
    }

    [Test]
    public void PolarToRectangularFadesSamplesOutsideTheSourceBounds()
    {
        var effect = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
        };
        (_, float[] pixels) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.White, s_size);

        // At u = 0.25 (3 o'clock) and v = 0.5, the sample lands past the right edge of the source.
        // At u = 0.75 (9 o'clock) and v = 0.5, the sample lands inside the left half of the source.
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, Width / 4, Height / 2, 3), Is.LessThan(0.001f), "sample outside source is transparent");
            Assert.That(Channel(pixels, (Width * 3) / 4, Height / 2, 3), Is.GreaterThan(0.99f), "sample inside source stays opaque");
        });
    }

    [Test]
    public void NonDefaultCenterAndAngleRoundTripOnTheInterior()
    {
        var center = new RelativePoint(0.42f, 0.58f, RelativeUnit.Relative);
        const float angle = 135f;
        var group = new FilterEffectGroup
        {
            Children =
            {
                new PolarCoordinates
                {
                    Center = { Value = center },
                    Angle = { Value = angle },
                    ClipToBounds = { Value = true },
                },
                new PolarCoordinates
                {
                    Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
                    Center = { Value = center },
                    Angle = { Value = angle },
                    ClipToBounds = { Value = true },
                },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // Stay inside the radius whose entire circle fits inside the bounds around the shifted center.
        float maximumError = 0f;
        for (int y = Height / 4; y < (Height * 3) / 4; y++)
        {
            for (int x = 12; x < Width - 12; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                    maximumError = MathF.Max(maximumError, MathF.Abs(Channel(after, x, y, channel) - Channel(before, x, y, channel)));
            }
        }

        Assert.That(maximumError, Is.LessThan(0.04f));
    }

    [TestCase(PolarCoordinatesDirection.RectangularToPolar)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular)]
    public void NonFiniteCenterAndAngleFallBackToDefaults(PolarCoordinatesDirection direction)
    {
        using PolarCoordinates.Resource expectedResource = new PolarCoordinates
        {
            Direction = { Value = direction },
        }.ToResource(CompositionContext.Default);
        using var invalidResource = new PolarCoordinates.Resource
        {
            Direction = direction,
            Amount = 100f,
            Center = new RelativePoint(float.NaN, float.PositiveInfinity, RelativeUnit.Relative),
            Angle = float.NaN,
            AspectMode = (PolarCoordinatesAspectMode)999,
            InnerRadius = float.NaN,
            OuterRadius = float.PositiveInfinity,
            Repeat = float.NaN,
            SweepAngle = float.NegativeInfinity,
            EdgeMode = (PolarCoordinatesEdgeMode)999,
        };

        (_, float[] expected) = FilterEffectTestHarness.RunOnTarget(expectedResource, s_size, DrawGradient);
        (_, float[] actual) = FilterEffectTestHarness.RunOnTarget(invalidResource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(actual.All(float.IsFinite), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    public void AspectModeContainAndCoverUseCircularRadii()
    {
        var contain = new PolarCoordinates { AspectMode = { Value = PolarCoordinatesAspectMode.Contain } };
        var cover = new PolarCoordinates { AspectMode = { Value = PolarCoordinatesAspectMode.Cover } };
        var containClipped = new PolarCoordinates
        {
            AspectMode = { Value = PolarCoordinatesAspectMode.Contain },
            ClipToBounds = { Value = true },
        };
        var coverClipped = new PolarCoordinates
        {
            AspectMode = { Value = PolarCoordinatesAspectMode.Cover },
            ClipToBounds = { Value = true },
        };

        (float[] containPixels, Rect containBounds, PixelSize containSize) = RunSolidWithBounds(contain, SKColors.White, s_size);
        (float[] coverPixels, Rect coverBounds, PixelSize coverSize) = RunSolidWithBounds(cover, SKColors.White, s_size);
        (_, float[] containClippedPixels) = FilterEffectTestHarness.RunOnSolidTarget(containClipped, SKColors.White, s_size);
        (_, float[] coverClippedPixels) = FilterEffectTestHarness.RunOnSolidTarget(coverClipped, SKColors.White, s_size);

        // Width=129 (half=64.5), Height=81 (half=40.5).
        // With ClipToBounds=false (default), Contain shrinks bounds to 81x81 and Cover expands bounds to 129x129.
        // With ClipToBounds=true, both keep the original 129x81 bounds.
        Assert.Multiple(() =>
        {
            Assert.That(containBounds, Is.EqualTo(new Rect(24, 0, Height, Height)));
            Assert.That(coverBounds, Is.EqualTo(new Rect(0, -24, Width, Width)));
            Assert.That(Channel(containPixels, containSize.Width, containSize.Width / 2, 1, 3), Is.GreaterThan(0.95f), "contain reaches top rim");
            Assert.That(Channel(containPixels, containSize.Width, containSize.Width - 2, containSize.Height / 2, 3), Is.GreaterThan(0.95f), "contain reaches right rim with no excess margins");
            Assert.That(Channel(coverPixels, coverSize.Width, coverSize.Width / 2, 1, 3), Is.GreaterThan(0.95f), "cover expands vertically without clipping top");
            Assert.That(Channel(coverPixels, coverSize.Width, coverSize.Width / 2, coverSize.Height - 2, 3), Is.GreaterThan(0.95f), "cover expands vertically without clipping bottom");
            Assert.That(Channel(coverPixels, coverSize.Width, 0, 0, 3), Is.LessThan(0.001f), "cover corner stays transparent");
            Assert.That(Channel(containClippedPixels, Width - 2, Height / 2, 3), Is.LessThan(0.001f), "clipped contain leaves horizontal margins transparent");
            Assert.That(Channel(coverClippedPixels, Width / 2 + 45, Height / 2 + 35, 3), Is.GreaterThan(0.95f), "clipped cover fills beyond inscribed ellipse");
        });
    }

    [Test]
    public void InnerAndOuterRadiusFormADonutRing()
    {
        var effect = new PolarCoordinates
        {
            InnerRadius = { Value = 40f },
            OuterRadius = { Value = 85f },
        };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, Rect bounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawTopBottomBands);

        // Output bounds fit the 85% outer radius around the center.
        int holeX = outSize.Width / 2;
        int cy = outSize.Height / 2;
        // Mid-ring lies halfway between 40% and 85% (radius 0.625 / 0.85 of half-width).
        int ringX = holeX + (int)(outSize.Width * 0.5f * (0.625f / 0.85f));

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.EqualTo(Width * 0.85f).Within(0.01f));
            Assert.That(bounds.Height, Is.EqualTo(Height * 0.85f).Within(0.01f));
            Assert.That(Channel(pixels, outSize.Width, holeX, cy, 3), Is.LessThan(0.001f), "inner hole is transparent");
            Assert.That(Channel(pixels, outSize.Width, ringX, cy, 1), Is.GreaterThan(0.95f), "middle of ring samples middle green band");
            Assert.That(Channel(pixels, outSize.Width, ringX, cy, 3), Is.GreaterThan(0.99f), "middle of ring is opaque");
            Assert.That(Channel(pixels, outSize.Width, 0, 0, 3), Is.LessThan(0.001f), "corner past outer radius is transparent");
        });
    }

    [Test]
    public void OuterRadiusAboveHundredExpandsOutputBoundsWithoutClipping()
    {
        var effect = new PolarCoordinates
        {
            OuterRadius = { Value = 150f },
        };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, Rect bounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawTopBottomBands);

        // Near the expanded outer rim (past the original 100% bounds), bottom row (blue) must render unclipped.
        int expandedRimX = outSize.Width - 3;
        int cy = outSize.Height / 2;

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.EqualTo(Width * 1.5f).Within(0.01f));
            Assert.That(bounds.Height, Is.EqualTo(Height * 1.5f).Within(0.01f));
            Assert.That(bounds.Center.X, Is.EqualTo(Width * 0.5f).Within(0.01f));
            Assert.That(bounds.Center.Y, Is.EqualTo(Height * 0.5f).Within(0.01f));
            Assert.That(Channel(pixels, outSize.Width, expandedRimX, cy, 2), Is.GreaterThan(0.95f), "expanded rim samples bottom blue band");
            Assert.That(Channel(pixels, outSize.Width, expandedRimX, cy, 3), Is.GreaterThan(0.95f), "expanded rim is opaque");
        });
    }

    [Test]
    public void AmountBelowHundredUnionsInputAndPolarBounds()
    {
        var transparent = new PolarCoordinates
        {
            AspectMode = { Value = PolarCoordinatesAspectMode.Cover },
            Amount = { Value = 50f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Transparent },
        };
        var mirrored = new PolarCoordinates
        {
            AspectMode = { Value = PolarCoordinatesAspectMode.Cover },
            Amount = { Value = 50f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Mirror },
        };

        (float[] transparentPixels, Rect transparentBounds, PixelSize outSize) = RunSolidWithBounds(transparent, SKColors.White, s_size);
        (float[] mirroredPixels, Rect mirroredBounds, _) = RunSolidWithBounds(mirrored, SKColors.White, s_size);

        var expectedBounds = new Rect(0, -24, Width, Width);
        Assert.Multiple(() =>
        {
            Assert.That(transparentBounds, Is.EqualTo(expectedBounds));
            Assert.That(mirroredBounds, Is.EqualTo(expectedBounds));
            // At the bottom of the expanded frame (logical y~104.5, mapped.y~80.4), Amount=50% places sample.y~92.4 past input height (81).
            Assert.That(Channel(transparentPixels, outSize.Width, outSize.Width / 2, outSize.Height - 1, 3), Is.LessThan(0.001f), "transparent fades out-of-input samples");
            Assert.That(Channel(mirroredPixels, outSize.Width, outSize.Width / 2, outSize.Height - 1, 3), Is.GreaterThan(0.99f), "mirror fills out-of-input samples");
        });
    }

    [Test]
    public void ClipToBoundsKeepsOriginalInputBounds()
    {
        var effect = new PolarCoordinates
        {
            AspectMode = { Value = PolarCoordinatesAspectMode.Cover },
            OuterRadius = { Value = 150f },
            Center = { Value = new RelativePoint(0.2f, 0.2f, RelativeUnit.Relative) },
            ClipToBounds = { Value = true },
        };
        (_, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);
        Assert.That(output, Is.EqualTo(s_bounds));
    }

    [Test]
    public void InvertRadiusSwapsInnerAndOuterRowsWhileKeepingCenterOpaque()
    {
        var effect = new PolarCoordinates { InvertRadius = { Value = true } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawTopBottomBands);

        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, Width / 2, Height / 2, 2), Is.GreaterThan(0.99f), "bottom row (blue) maps to center");
            Assert.That(Channel(pixels, Width / 2, Height / 2, 3), Is.GreaterThan(0.99f), "center remains opaque");
            Assert.That(Channel(pixels, Width / 2, 1, 0), Is.GreaterThan(0.95f), "top row (red) maps to outer rim");
            Assert.That(Channel(pixels, 0, 0, 3), Is.LessThan(0.001f), "corner past rim stays transparent");
        });
    }

    [Test]
    public void ReverseAngleMapsQuadrantsCounterClockwise()
    {
        var effect = new PolarCoordinates { ReverseAngle = { Value = true } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawQuadrantStripes);

        // Counter-clockwise from 12 o'clock:
        // R: 12..9 (top-left), G: 9..6 (bottom-left), B: 6..3 (bottom-right), W: 3..12 (top-right).
        int tlX = Width / 2 - Width / 6;
        int tlY = Height / 2 - Height / 6;
        int blX = Width / 2 - Width / 6;
        int blY = Height / 2 + Height / 6;
        int brX = Width / 2 + Width / 6;
        int brY = Height / 2 + Height / 6;

        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, tlX, tlY, 0), Is.GreaterThan(0.95f), "top-left red");
            Assert.That(Channel(pixels, tlX, tlY, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, blX, blY, 1), Is.GreaterThan(0.95f), "bottom-left green");
            Assert.That(Channel(pixels, blX, blY, 0), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, brX, brY, 2), Is.GreaterThan(0.95f), "bottom-right blue");
            Assert.That(Channel(pixels, brX, brY, 1), Is.LessThan(0.05f));
        });
    }

    [Test]
    public void RepeatAndMirrorTileAngularlyInBothDirections()
    {
        var repeatOnly = new PolarCoordinates { Repeat = { Value = 2f } };
        var repeatMirrored = new PolarCoordinates { Repeat = { Value = 2f }, Mirror = { Value = true } };
        using PolarCoordinates.Resource repeatResource = repeatOnly.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource mirrorResource = repeatMirrored.ToResource(CompositionContext.Default);

        (_, float[] repeatPixels) = FilterEffectTestHarness.RunOnTarget(repeatResource, s_size, DrawQuadrantStripes);
        (_, float[] mirrorPixels) = FilterEffectTestHarness.RunOnTarget(mirrorResource, s_size, DrawQuadrantStripes);

        // With Repeat=2, 12..6 o'clock covers u=[0, 1] and 6..12 o'clock covers a second cycle.
        // Without Mirror: 1:30 (top-right) and 7:30 (bottom-left) both hit the first half (R/G),
        // specifically 12:45 (u~0.125 -> R) matches 6:45 (u~0.125 -> R).
        // With Mirror: the second cycle runs backwards, so 12:45 (R) mirrors to 11:15 (R) across the vertical axis.
        int dxSmall = 8;
        int dyNearTop = -Height / 3;
        int dyNearBottom = Height / 3;

        var p2rMirror = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Repeat = { Value = 2f },
            Mirror = { Value = true },
        };
        using PolarCoordinates.Resource p2rResource = p2rMirror.ToResource(CompositionContext.Default);
        (_, float[] p2rPixels) = FilterEffectTestHarness.RunOnTarget(p2rResource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(Channel(repeatPixels, Width / 2 + dxSmall, Height / 2 + dyNearTop, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(repeatPixels, Width / 2 - dxSmall, Height / 2 + dyNearBottom, 0), Is.GreaterThan(0.95f), "non-mirrored 2nd cycle repeats from start");
            Assert.That(Channel(mirrorPixels, Width / 2 + dxSmall, Height / 2 + dyNearTop, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(mirrorPixels, Width / 2 - dxSmall, Height / 2 + dyNearTop, 0), Is.GreaterThan(0.95f), "mirrored 2nd cycle reflects across 12 o'clock");
            // In PolarToRectangular with Repeat=2 & Mirror=true, x=Width/4 and x=3*Width/4 sample the same mirrored angle.
            Assert.That(Channel(p2rPixels, Width / 4, Height / 2, 0), Is.EqualTo(Channel(p2rPixels, (Width * 3) / 4, Height / 2, 0)).Within(0.02f));
        });
    }

    [Test]
    public void SweepAngleClipsToArcAndKeepsRepeatedInteriorSeamOpaque()
    {
        var halfArc = new PolarCoordinates
        {
            SweepAngle = { Value = 180f },
            Repeat = { Value = 2f },
        };
        (_, float[] pixels) = FilterEffectTestHarness.RunOnSolidTarget(halfArc, SKColors.White, s_size);

        // Right half (3 o'clock is the interior repeat seam at uArc = 0.5) must stay opaque; left half must be transparent.
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, Width / 2 + Width / 4, Height / 2, 3), Is.GreaterThan(0.99f), "interior repeat seam inside arc stays opaque");
            Assert.That(Channel(pixels, Width / 2 - Width / 4, Height / 2, 3), Is.LessThan(0.001f), "outside arc is transparent");
        });
    }

    [TestCase(PolarCoordinatesEdgeMode.Clamp)]
    [TestCase(PolarCoordinatesEdgeMode.Repeat)]
    [TestCase(PolarCoordinatesEdgeMode.Mirror)]
    public void NonTransparentEdgeModesFillOutsideTheRingAndBounds(PolarCoordinatesEdgeMode edgeMode)
    {
        var r2p = new PolarCoordinates
        {
            InnerRadius = { Value = 35f },
            OuterRadius = { Value = 75f },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = edgeMode },
        };
        var p2r = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
            EdgeMode = { Value = edgeMode },
        };

        (_, float[] r2pPixels) = FilterEffectTestHarness.RunOnSolidTarget(r2p, SKColors.White, s_size);
        (_, float[] p2rPixels) = FilterEffectTestHarness.RunOnSolidTarget(p2r, SKColors.White, s_size);

        Assert.Multiple(() =>
        {
            Assert.That(Channel(r2pPixels, Width / 2, Height / 2, 3), Is.GreaterThan(0.99f), $"inner hole filled ({edgeMode})");
            Assert.That(Channel(r2pPixels, 0, 0, 3), Is.GreaterThan(0.99f), $"outer corner filled ({edgeMode})");
            Assert.That(Channel(r2pPixels, Width / 2 - Width / 4, Height / 2, 3), Is.GreaterThan(0.99f), $"outside arc filled ({edgeMode})");
            Assert.That(Channel(p2rPixels, Width / 4, Height / 2, 3), Is.GreaterThan(0.99f), $"polar-to-rect outside bounds filled ({edgeMode})");
        });
    }

    [Test]
    public void ClampEdgeModePreservesRightEdgeColorPastArcEnd()
    {
        var effect = new PolarCoordinates
        {
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Clamp },
        };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawQuadrantStripes);

        // Just past the 6 o'clock end of the 180-degree arc (around 7:30, bottom-left), Clamp must sample u=1 (White), not wrap to u=0 (Red).
        int x = Width / 2 - Width / 6;
        int y = Height / 2 + Height / 4;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, x, y, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, x, y, 1), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, x, y, 2), Is.GreaterThan(0.95f));
        });
    }

    [TestCase(PolarCoordinatesEdgeMode.Transparent)]
    [TestCase(PolarCoordinatesEdgeMode.Clamp)]
    public void DegenerateRadiusAndSweepStayFinite(PolarCoordinatesEdgeMode edgeMode)
    {
        var effect = new PolarCoordinates
        {
            InnerRadius = { Value = 50f },
            OuterRadius = { Value = 50f },
            SweepAngle = { Value = 0f },
            EdgeMode = { Value = edgeMode },
        };
        (float[] pixels, _, PixelSize outSize) = RunSolidWithBounds(effect, SKColors.White, s_size);

        Assert.Multiple(() =>
        {
            Assert.That(pixels.All(float.IsFinite), Is.True);
            if (edgeMode == PolarCoordinatesEdgeMode.Transparent)
                Assert.That(Channel(pixels, outSize.Width, outSize.Width / 2, outSize.Height / 2, 3), Is.LessThan(0.001f));
        });
    }

    [Test]
    public void ExtendedParametersRoundTripBetweenDirections()
    {
        var center = new RelativePoint(0.46f, 0.52f, RelativeUnit.Relative);
        var group = new FilterEffectGroup
        {
            Children =
            {
                new PolarCoordinates
                {
                    Center = { Value = center },
                    Angle = { Value = 40f },
                    AspectMode = { Value = PolarCoordinatesAspectMode.Contain },
                    InnerRadius = { Value = 20f },
                    OuterRadius = { Value = 90f },
                    InvertRadius = { Value = true },
                    ReverseAngle = { Value = true },
                    SweepAngle = { Value = 240f },
                    ClipToBounds = { Value = true },
                },
                new PolarCoordinates
                {
                    Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
                    Center = { Value = center },
                    Angle = { Value = 40f },
                    AspectMode = { Value = PolarCoordinatesAspectMode.Contain },
                    InnerRadius = { Value = 20f },
                    OuterRadius = { Value = 90f },
                    InvertRadius = { Value = true },
                    ReverseAngle = { Value = true },
                    SweepAngle = { Value = 240f },
                    ClipToBounds = { Value = true },
                },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        float maximumError = 0f;
        for (int y = Height / 4; y < (Height * 3) / 4; y++)
        {
            for (int x = 16; x < Width - 16; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                    maximumError = MathF.Max(maximumError, MathF.Abs(Channel(after, x, y, channel) - Channel(before, x, y, channel)));
            }
        }

        Assert.That(maximumError, Is.LessThan(0.04f));
    }

    [Test]
    public void HitTestRespectsDonutHoleArcAndEdgeMode()
    {
        var transparentDonut = new PolarCoordinates
        {
            InnerRadius = { Value = 40f },
            OuterRadius = { Value = 80f },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Transparent },
        };
        (ShaderDescription tDesc, Rect tOutput) = FilterEffectTestHarness.RecordShader(transparentDonut, s_bounds);
        RenderHitTestContract tHit = tDesc.HitTest!.Value;
        var fullInput = new RenderHitTestInput(s_bounds, s_bounds.ContainsExclusive);

        var clampedDonut = new PolarCoordinates
        {
            InnerRadius = { Value = 40f },
            OuterRadius = { Value = 80f },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Clamp },
        };
        (ShaderDescription cDesc, Rect cOutput) = FilterEffectTestHarness.RecordShader(clampedDonut, s_bounds);
        RenderHitTestContract cHit = cDesc.HitTest!.Value;

        var centerPoint = s_bounds.Center;
        var insideRingRight = new Point(s_bounds.Center.X + s_bounds.Width * 0.3f, s_bounds.Center.Y);
        var outsideArcLeft = new Point(s_bounds.Center.X - s_bounds.Width * 0.3f, s_bounds.Center.Y);

        Assert.Multiple(() =>
        {
            Assert.That(tHit.Evaluate(tOutput, [fullInput], [], centerPoint), Is.False, "transparent hole misses");
            Assert.That(tHit.Evaluate(tOutput, [fullInput], [], insideRingRight), Is.True, "ring inside arc hits");
            Assert.That(tHit.Evaluate(tOutput, [fullInput], [], outsideArcLeft), Is.False, "outside arc misses");
            Assert.That(cHit.Evaluate(cOutput, [fullInput], [], centerPoint), Is.True, "clamped hole hits");
            Assert.That(cHit.Evaluate(cOutput, [fullInput], [], outsideArcLeft), Is.True, "clamped outside arc hits");
        });
    }

    [Test]
    public void HitTestRespectsCenterAngleAndOutOfBoundsSamples()
    {
        var center = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);
        var rectToPolar = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.RectangularToPolar },
            Center = { Value = center },
            Angle = { Value = 90f },
        };
        (ShaderDescription r2pDesc, Rect r2pOutput) = FilterEffectTestHarness.RecordShader(rectToPolar, s_bounds);
        RenderHitTestContract r2pHit = r2pDesc.HitTest!.Value;
        // Center is at x=25, y=30 in local space. Point (50, 30) is 3 o'clock at radius 0.5.
        // With Angle=90, 3 o'clock maps to u=0, v=0.5 -> source local (0, 30).
        var r2pAccepted = new Rect(s_bounds.X - 1, s_bounds.Center.Y - 1, 2, 2);
        var r2pInput = new RenderHitTestInput(s_bounds, r2pAccepted.ContainsExclusive);
        var r2pPoint = new Point(s_bounds.X + 50, s_bounds.Center.Y);

        var polarToRect = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
            Angle = { Value = 90f },
        };
        (ShaderDescription p2rDesc, Rect p2rOutput) = FilterEffectTestHarness.RecordShader(polarToRect, s_bounds);
        RenderHitTestContract p2rHit = p2rDesc.HitTest!.Value;
        // At u=0.5, Angle=90 -> total angle=270 deg (9 o'clock), v=0.5 -> local (100 - 25, 30) = (75, 30).
        var p2rAccepted = new Rect(s_bounds.X + 74, s_bounds.Center.Y - 1, 2, 2);
        var p2rInput = new RenderHitTestInput(s_bounds, p2rAccepted.ContainsExclusive);
        var p2rInsidePoint = new Point(s_bounds.X + 50, s_bounds.Center.Y);
        // At u=0, Angle=90 -> total angle=90 deg (3 o'clock), v=0.5 -> local (125, 30), outside bounds.
        var p2rOutsidePoint = new Point(s_bounds.X + 0.5f, s_bounds.Center.Y);

        Assert.Multiple(() =>
        {
            Assert.That(r2pOutput, Is.EqualTo(new Rect(s_bounds.X - 25f, s_bounds.Y, s_bounds.Width, s_bounds.Height)));
            Assert.That(r2pHit.Evaluate(r2pOutput, [r2pInput], [], r2pPoint), Is.True);
            // Point outside original s_bounds on the left, yet inside the expanded output bounds around the shifted center.
            Assert.That(r2pHit.Evaluate(r2pOutput, [new RenderHitTestInput(s_bounds, _ => true)], [], new Point(s_bounds.X - 20f, s_bounds.Center.Y)), Is.True);
            Assert.That(r2pHit.Evaluate(r2pOutput, [new RenderHitTestInput(s_bounds, _ => true)], [], new Point(s_bounds.X + 95, s_bounds.Center.Y)), Is.False);
            Assert.That(p2rHit.Evaluate(p2rOutput, [p2rInput], [], p2rInsidePoint), Is.True);
            Assert.That(p2rHit.Evaluate(p2rOutput, [new RenderHitTestInput(s_bounds, _ => true)], [], p2rOutsidePoint), Is.False);
        });
    }

    private static (float[] Pixels, Rect OutputBounds, PixelSize OutputSize) RunSolidWithBounds(
        FilterEffect effect, SKColor color, PixelSize size)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        return RunWithBounds(resource, size, canvas => canvas.Clear(color));
    }

    private static (float[] Pixels, Rect OutputBounds, PixelSize OutputSize) RunWithBounds(
        FilterEffect.Resource resource, PixelSize size, Action<SKCanvas> draw)
    {
        var bounds = new Rect(0, 0, size.Width, size.Height);
        using RenderTarget backing = CpuRenderTargets.Create(size.Width, size.Height);
        draw(backing.Value.Canvas);
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                bounds,
                EffectiveScale.At(1),
                new PixelRect(0, 0, size.Width, size.Height)),
        };
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        executor.Apply(context);
        executor.Flush(false);

        EffectTarget outputTarget = targets.Single();
        using Bitmap afterBitmap = outputTarget.RenderTarget!.Snapshot();
        float[] pixels = afterBitmap.GetPixelSpan<ushort>()
            .ToArray()
            .Select(static bits => (float)BitConverter.UInt16BitsToHalf(bits))
            .ToArray();
        return (pixels, outputTarget.Bounds, new PixelSize(afterBitmap.Width, afterBitmap.Height));
    }

    private static void DrawTopBottomBands(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Lime);
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(0, 0, Width, 6), paint);
        paint.Color = SKColors.Blue;
        canvas.DrawRect(new SKRect(0, Height - 6, Width, Height), paint);
    }

    private static void DrawQuadrantStripes(SKCanvas canvas)
    {
        SKColor[] colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White];
        float bandWidth = Width / 4f;
        using var paint = new SKPaint();
        for (int i = 0; i < colors.Length; i++)
        {
            paint.Color = colors[i];
            canvas.DrawRect(new SKRect(i * bandWidth, 0, (i + 1) * bandWidth, Height), paint);
        }
    }

    private static void DrawGradient(SKCanvas canvas)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(Width, Height), [SKColors.Black, SKColors.White], [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
    }

    private static float Channel(float[] pixels, int x, int y, int channel) => Channel(pixels, Width, x, y, channel);

    private static float Channel(float[] pixels, int stride, int x, int y, int channel) => pixels[((y * stride + x) * 4) + channel];
}

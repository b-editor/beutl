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
    public void RectangularToPolarMapsTopOutsideBottomInsideAndResizesToSquare()
    {
        using PolarCoordinates.Resource resource = new PolarCoordinates().ToResource(CompositionContext.Default);
        (float[] pixels, Rect bounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawTopBottomBands);

        // outerR = (Width + Height) / pi = 210 / pi (~66.85), diameter ~ 133.69, centered at (64.5, 40.5).
        // Default orientation places the bottom row (blue) at the center and the top row (red) on the outer rim.
        float expectedDiameter = ((Width + Height) / MathF.PI) * 2f;
        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.EqualTo(expectedDiameter).Within(0.05f));
            Assert.That(bounds.Height, Is.EqualTo(expectedDiameter).Within(0.05f));
            Assert.That(bounds.Center.X, Is.EqualTo(Width * 0.5f).Within(0.01f));
            Assert.That(bounds.Center.Y, Is.EqualTo(Height * 0.5f).Within(0.01f));
            Assert.That(Channel(pixels, outSize.Width, cx, cy, 2), Is.GreaterThan(0.99f), "bottom row (blue) at the center");
            Assert.That(Channel(pixels, outSize.Width, cx, cy, 3), Is.GreaterThan(0.99f), "center is opaque");
            foreach ((int x, int y) in new[] { (cx, 2), (2, cy), (outSize.Width - 3, cy), (cx, outSize.Height - 3) })
                Assert.That(Channel(pixels, outSize.Width, x, y, 0), Is.GreaterThan(0.95f), $"top row (red) at outer rim ({x}, {y})");
            foreach ((int x, int y) in new[] { (0, 0), (outSize.Width - 1, 0), (0, outSize.Height - 1), (outSize.Width - 1, outSize.Height - 1) })
                Assert.That(Channel(pixels, outSize.Width, x, y, 3), Is.LessThan(0.001f), $"transparent corner ({x}, {y})");
        });
    }

    [Test]
    public void AmountBlendsTheSamplingCoordinates()
    {
        var effect = new PolarCoordinates { Amount = { Value = 50f }, ClipToBounds = { Value = true } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // At the center (v=1 -> bottom-left (0, Height)), 50% blends halfway between (Width/2, Height/2) and (0, Height): (Width/4, 3*Height/4).
        float expected = Channel(before, Width / 4, (Height * 3) / 4, 0);
        Assert.That(Channel(after, Width / 2, Height / 2, 0), Is.EqualTo(expected).Within(0.01f));
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
        (float[] before, Rect _, PixelSize _) = RunWithBounds(new FilterEffectGroup().ToResource(CompositionContext.Default), s_size, DrawGradient);
        (float[] after, Rect outBounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawGradient);

        // Stage 1 produces rOuter = (Width + Height) / pi. Stage 2 unwraps r=[0, rOuter] into height=rOuter, width=pi*rOuter = Width + Height.
        // Compare normalized (u, v) points across the interior against the original source gradient.
        float expectedOuterR = (Width + Height) / MathF.PI;
        float maximumError = 0f;
        for (int y = 10; y < outSize.Height - 14; y++)
        {
            int srcY = Math.Clamp((int)MathF.Round((y + 0.5f) / outSize.Height * Height - 0.5f), 0, Height - 1);
            for (int x = 16; x < outSize.Width - 16; x++)
            {
                int srcX = Math.Clamp((int)MathF.Round((x + 0.5f) / outSize.Width * Width - 0.5f), 0, Width - 1);
                for (int channel = 0; channel < 4; channel++)
                {
                    maximumError = MathF.Max(
                        maximumError,
                        MathF.Abs(Channel(after, outSize.Width, x, y, channel) - Channel(before, Width, srcX, srcY, channel)));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(outBounds.Height, Is.EqualTo(expectedOuterR).Within(0.05f));
            Assert.That(outBounds.Width, Is.EqualTo(Width + Height).Within(0.1f));
            Assert.That(maximumError, Is.LessThan(0.04f));
        });
    }

    [Test]
    public void ResizesOutputBoundsAndRequiresTheFullSource()
    {
        var r2p = new PolarCoordinates();
        var p2r = new PolarCoordinates { Direction = { Value = PolarCoordinatesDirection.PolarToRectangular } };
        var r2pClipped = new PolarCoordinates { ClipToBounds = { Value = true } };
        var p2rClipped = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            ClipToBounds = { Value = true },
        };

        (ShaderDescription r2pDesc, Rect r2pOut) = FilterEffectTestHarness.RecordShader(r2p, s_bounds);
        (ShaderDescription p2rDesc, Rect p2rOut) = FilterEffectTestHarness.RecordShader(p2r, s_bounds);
        (_, Rect r2pClippedOut) = FilterEffectTestHarness.RecordShader(r2pClipped, s_bounds);
        (_, Rect p2rClippedOut) = FilterEffectTestHarness.RecordShader(p2rClipped, s_bounds);

        // s_bounds = (10.25, -20.5, 100, 60), center = (60.25, 9.5).
        // RectangularToPolar: rOuter = (100 + 60) / pi -> diameter = 320 / pi centered at (60.25, 9.5).
        // PolarToRectangular: baseOuter = min(100, 60)/2 = 30 -> height=30, width=2*pi*15 centered at (60.25, 9.5).
        float expectedR2pDiameter = ((s_bounds.Width + s_bounds.Height) / MathF.PI) * 2f;
        Assert.Multiple(() =>
        {
            Assert.That(r2pOut.Width, Is.EqualTo(expectedR2pDiameter).Within(0.05f));
            Assert.That(r2pOut.Height, Is.EqualTo(expectedR2pDiameter).Within(0.05f));
            Assert.That(r2pOut.Center.X, Is.EqualTo(s_bounds.Center.X).Within(0.01f));
            Assert.That(r2pOut.Center.Y, Is.EqualTo(s_bounds.Center.Y).Within(0.01f));
            Assert.That(p2rOut.Height, Is.EqualTo(30f).Within(0.01f));
            Assert.That(p2rOut.Width, Is.EqualTo(MathF.Tau * 15f).Within(0.05f));
            Assert.That(p2rOut.Center.X, Is.EqualTo(s_bounds.Center.X).Within(0.01f));
            Assert.That(p2rOut.Center.Y, Is.EqualTo(s_bounds.Center.Y).Within(0.01f));
            Assert.That(r2pClippedOut, Is.EqualTo(s_bounds));
            Assert.That(p2rClippedOut, Is.EqualTo(s_bounds));
            Assert.That(r2pDesc.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(p2rDesc.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(r2pDesc.Bounds.RequiresFullInput, Is.True);
            Assert.That(p2rDesc.Bounds.RequiresFullInput, Is.True);
            Assert.That(r2pDesc.SourceTileMode, Is.EqualTo(SKShaderTileMode.Decal));
            Assert.That(p2rDesc.SourceTileMode, Is.EqualTo(SKShaderTileMode.Decal));
        });
    }

    [Test]
    public void TwelveOClockSeamIsOpaque()
    {
        (float[] pixels, _, PixelSize outSize) = RunSolidWithBounds(new PolarCoordinates(), SKColors.White, s_size);

        int cx = outSize.Width / 2;
        for (int y = 4; y < outSize.Height / 2 - 4; y++)
        {
            for (int x = cx - 1; x <= cx + 1; x++)
                Assert.That(Channel(pixels, outSize.Width, x, y, 3), Is.GreaterThan(0.99f), $"seam pixel ({x}, {y})");
        }
    }

    [Test]
    public void HitTestUsesTheMappedSourcePoint()
    {
        var r2p = new PolarCoordinates();
        (ShaderDescription r2pDesc, Rect r2pOut) = FilterEffectTestHarness.RecordShader(r2p, s_bounds);
        RenderHitTestContract r2pHit = r2pDesc.HitTest!.Value;
        // s_bounds=(10.25, -20.5, 100, 60), center=(60.25, 9.5), rOuter = 160 / pi.
        // Point at 3 o'clock halfway out: (center.X + rOuter/2, center.Y) -> u=0.25, v=0.5 -> source local (25, 30).
        float expectedOuterR = (s_bounds.Width + s_bounds.Height) / MathF.PI;
        var r2pAccepted = new Rect(s_bounds.X + 24f, s_bounds.Center.Y - 1f, 2f, 2f);
        var r2pInput = new RenderHitTestInput(s_bounds, r2pAccepted.ContainsExclusive);
        var r2pPoint = new Point(s_bounds.Center.X + expectedOuterR * 0.5f, s_bounds.Center.Y);

        var p2r = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            ClipToBounds = { Value = true },
        };
        (ShaderDescription p2rDesc, Rect p2rOut) = FilterEffectTestHarness.RecordShader(p2r, s_bounds);
        RenderHitTestContract p2rHit = p2rDesc.HitTest!.Value;
        // With ClipToBounds=true, u=0.25 (3 o'clock), v=0.5 -> r=15 -> source (center.X + 15, center.Y).
        var p2rAccepted = new Rect(s_bounds.Center.X + 14f, s_bounds.Center.Y - 1f, 2f, 2f);
        var p2rInput = new RenderHitTestInput(s_bounds, p2rAccepted.ContainsExclusive);
        var p2rPoint = new Point(s_bounds.X + 25f, s_bounds.Center.Y);

        Assert.Multiple(() =>
        {
            Assert.That(r2pHit.Evaluate(r2pOut, [r2pInput], [], r2pPoint), Is.True);
            Assert.That(r2pHit.Evaluate(r2pOut, [r2pInput], [], r2pPoint + new Vector(0, 10)), Is.False);
            Assert.That(r2pHit.Evaluate(r2pOut, [new RenderHitTestInput(s_bounds, _ => true)], [], r2pOut.BottomRight), Is.False);
            Assert.That(p2rHit.Evaluate(p2rOut, [p2rInput], [], p2rPoint), Is.True);
            Assert.That(p2rHit.Evaluate(p2rOut, [p2rInput], [], p2rPoint + new Vector(0, 10)), Is.False);
        });
    }

    [TestCase(90f)]
    [TestCase(450f)]
    [TestCase(-270f)]
    public void AngleRotatesRectangularToPolarClockwise(float angle)
    {
        var effect = new PolarCoordinates { Angle = { Value = angle } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, _, PixelSize outSize) = RunWithBounds(resource, s_size, DrawQuadrantStripes);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int d = outSize.Width / 4;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy + d, 0), Is.GreaterThan(0.95f), "3..6 o'clock red");
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy + d, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy + d, 1), Is.GreaterThan(0.95f), "6..9 o'clock green");
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy + d, 0), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy - d, 2), Is.GreaterThan(0.95f), "9..12 o'clock blue");
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy - d, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy - d, 0), Is.GreaterThan(0.95f), "12..3 o'clock white");
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy - d, 1), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy - d, 2), Is.GreaterThan(0.95f));
        });
    }

    [Test]
    public void SwirlTwistsAngleClockwiseFromInnerToOuter()
    {
        var effect = new PolarCoordinates { Swirl = { Value = 90f } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, _, PixelSize outSize) = RunWithBounds(resource, s_size, DrawQuadrantStripes);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        // Swirl anchors the outer rim (vLinear=1 -> 0 deg) and twists inward by -90 deg at the center:
        // Near outer (diagonal offset 40 -> r ~ 56.6 ~ 0.85 of 66.85): 1:30 stays red.
        // Near inner (diagonal offset 8 -> r ~ 11.3 ~ 0.17 of 66.85): 10:30 twists into red and 1:30 twists into green.
        int rInnerSample = 8;
        int rOuterSample = 40;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, cx + rOuterSample, cy - rOuterSample, 0), Is.GreaterThan(0.95f), "outer 1:30 stays red");
            Assert.That(Channel(pixels, outSize.Width, cx + rOuterSample, cy - rOuterSample, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx - rInnerSample, cy - rInnerSample, 0), Is.GreaterThan(0.95f), "inner 10:30 twists into red");
            Assert.That(Channel(pixels, outSize.Width, cx - rInnerSample, cy - rInnerSample, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx + rInnerSample, cy - rInnerSample, 1), Is.GreaterThan(0.95f), "inner 1:30 twists into green");
            Assert.That(Channel(pixels, outSize.Width, cx + rInnerSample, cy - rInnerSample, 0), Is.LessThan(0.05f));
        });
    }

    [Test]
    public void InnerRadiusExpandsRingWithAviUtlProportions()
    {
        const float inner = 40f;
        var effect = new PolarCoordinates { InnerRadius = { Value = inner } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, Rect bounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawTopBottomBands);

        // rOuter = (129 + 81 + 40) / pi (~79.58), rInner = rOuter * 40 / (81 + 40) (~26.31).
        float expectedOuterR = (Width + Height + inner) / MathF.PI;
        float expectedInnerR = expectedOuterR * (inner / (Height + inner));
        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int innerSampleX = cx + (int)MathF.Ceiling(expectedInnerR) + 2;
        int midSampleX = cx + (int)MathF.Round((expectedInnerR + expectedOuterR) * 0.5f);
        int outerSampleX = cx + (int)MathF.Floor(expectedOuterR) - 2;
        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.EqualTo(expectedOuterR * 2f).Within(0.05f));
            Assert.That(bounds.Height, Is.EqualTo(expectedOuterR * 2f).Within(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx, cy, 3), Is.LessThan(0.001f), "center hole is transparent");
            Assert.That(Channel(pixels, outSize.Width, innerSampleX, cy, 2), Is.GreaterThan(0.95f), "inner rim samples bottom blue band");
            Assert.That(Channel(pixels, outSize.Width, midSampleX, cy, 1), Is.GreaterThan(0.95f), "mid-ring samples middle green band");
            Assert.That(Channel(pixels, outSize.Width, outerSampleX, cy, 0), Is.GreaterThan(0.95f), "outer rim samples top red band");
            Assert.That(Channel(pixels, outSize.Width, 0, 0, 3), Is.LessThan(0.001f), "outside outer rim is transparent");
        });
    }

    [Test]
    public void ScaleScalesInnerAndOuterRadiiUniformly()
    {
        const float inner = 40f;
        var larger = new PolarCoordinates { InnerRadius = { Value = inner }, Scale = { Value = 150f } };
        var smaller = new PolarCoordinates { InnerRadius = { Value = inner }, Scale = { Value = 50f } };
        var zero = new PolarCoordinates { InnerRadius = { Value = inner }, Scale = { Value = 0f } };

        (float[] largerPixels, Rect largerBounds, PixelSize largerSize) = RunSolidWithBounds(larger, SKColors.White, s_size);
        (_, Rect smallerBounds, _) = RunSolidWithBounds(smaller, SKColors.White, s_size);
        (float[] zeroPixels, Rect zeroBounds, _) = RunSolidWithBounds(zero, SKColors.White, s_size);

        float baseDiameter = ((Width + Height + inner) / MathF.PI) * 2f;
        int cx = largerSize.Width / 2;
        int cy = largerSize.Height / 2;
        int ringOffset = (int)(baseDiameter * 1.5f * 0.35f);
        Assert.Multiple(() =>
        {
            Assert.That(largerBounds.Width, Is.EqualTo(baseDiameter * 1.5f).Within(0.05f));
            Assert.That(largerBounds.Height, Is.EqualTo(baseDiameter * 1.5f).Within(0.05f));
            Assert.That(smallerBounds.Width, Is.EqualTo(baseDiameter * 0.5f).Within(0.05f));
            Assert.That(smallerBounds.Height, Is.EqualTo(baseDiameter * 0.5f).Within(0.05f));
            Assert.That(Channel(largerPixels, largerSize.Width, cx, cy, 3), Is.LessThan(0.001f), "scaled inner hole stays transparent");
            Assert.That(Channel(largerPixels, largerSize.Width, cx + ringOffset, cy, 3), Is.GreaterThan(0.99f), "scaled ring is opaque");
            Assert.That(zeroBounds, Is.EqualTo(new Rect(0, 0, Width, Height)));
            Assert.That(zeroPixels.All(float.IsFinite), Is.True);
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
            Assert.That(relativeBounds.Center.X, Is.EqualTo(Width * 0.25f).Within(0.01f), "bounds center follows shifted pole X");
            Assert.That(relativeBounds.Center.Y, Is.EqualTo(Height * 0.25f).Within(0.01f), "bounds center follows shifted pole Y");
            Assert.That(absoluteBounds, Is.EqualTo(relativeBounds));
            Assert.That(Channel(relativePixels, relativeSize.Width, relativeSize.Width / 2, relativeSize.Height / 2, 2), Is.GreaterThan(0.95f), "bottom row (blue) maps to shifted pole");
            Assert.That(Channel(relativePixels, relativeSize.Width, relativeSize.Width / 2, 2, 0), Is.GreaterThan(0.95f), "top row (red) at unclipped top rim");
            Assert.That(absolutePixels, Is.EqualTo(relativePixels).Within(0.001f));
            Assert.That(Channel(clippedPixels, cx, cy, 2), Is.GreaterThan(0.95f), "clipped mode keeps pole at shifted local point");
        });
    }

    [Test]
    public void PolarToRectangularFadesSamplesOutsideTheSourceBounds()
    {
        var effect = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
            ClipToBounds = { Value = true },
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

    [TestCase(PolarCoordinatesDirection.RectangularToPolar)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular)]
    public void NonFiniteAndInvalidValuesFallBackToDefaults(PolarCoordinatesDirection direction)
    {
        using PolarCoordinates.Resource expectedResource = new PolarCoordinates
        {
            Direction = { Value = direction },
        }.ToResource(CompositionContext.Default);
        using var invalidResource = new PolarCoordinates.Resource
        {
            Direction = direction,
            Amount = 100f,
            InnerRadius = float.NaN,
            Scale = float.PositiveInfinity,
            Angle = float.NaN,
            Swirl = float.NegativeInfinity,
            SweepAngle = float.NegativeInfinity,
            Center = new RelativePoint(float.NaN, float.PositiveInfinity, RelativeUnit.Relative),
            Repeat = float.NaN,
            EdgeMode = (PolarCoordinatesEdgeMode)999,
        };

        (float[] expected, Rect expectedBounds, _) = RunWithBounds(expectedResource, s_size, DrawGradient);
        (float[] actual, Rect actualBounds, _) = RunWithBounds(invalidResource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(actualBounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.All(float.IsFinite), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    public void InvertRadiusSwapsInnerAndOuterRowsWhileKeepingCenterOpaque()
    {
        var effect = new PolarCoordinates { InvertRadius = { Value = true } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, _, PixelSize outSize) = RunWithBounds(resource, s_size, DrawTopBottomBands);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, cx, cy, 0), Is.GreaterThan(0.99f), "top row (red) maps to center");
            Assert.That(Channel(pixels, outSize.Width, cx, cy, 3), Is.GreaterThan(0.99f), "center remains opaque");
            Assert.That(Channel(pixels, outSize.Width, cx, 1, 2), Is.GreaterThan(0.95f), "bottom row (blue) maps to outer rim");
            Assert.That(Channel(pixels, outSize.Width, 0, 0, 3), Is.LessThan(0.001f), "corner past rim stays transparent");
        });
    }

    [Test]
    public void ReverseAngleMapsQuadrantsCounterClockwise()
    {
        var effect = new PolarCoordinates { ReverseAngle = { Value = true } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] pixels, _, PixelSize outSize) = RunWithBounds(resource, s_size, DrawQuadrantStripes);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int d = outSize.Width / 4;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy - d, 0), Is.GreaterThan(0.95f), "top-left red");
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy - d, 1), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy + d, 1), Is.GreaterThan(0.95f), "bottom-left green");
            Assert.That(Channel(pixels, outSize.Width, cx - d, cy + d, 0), Is.LessThan(0.05f));
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy + d, 2), Is.GreaterThan(0.95f), "bottom-right blue");
            Assert.That(Channel(pixels, outSize.Width, cx + d, cy + d, 1), Is.LessThan(0.05f));
        });
    }

    [Test]
    public void RepeatAndMirrorTileAngularlyInBothDirections()
    {
        var repeatOnly = new PolarCoordinates { Repeat = { Value = 2f } };
        var repeatMirrored = new PolarCoordinates { Repeat = { Value = 2f }, Mirror = { Value = true } };
        using PolarCoordinates.Resource repeatResource = repeatOnly.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource mirrorResource = repeatMirrored.ToResource(CompositionContext.Default);

        (float[] repeatPixels, _, PixelSize outSize) = RunWithBounds(repeatResource, s_size, DrawQuadrantStripes);
        (float[] mirrorPixels, _, _) = RunWithBounds(mirrorResource, s_size, DrawQuadrantStripes);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int dxSmall = 8;
        int dy = outSize.Height / 3;

        var p2rMirror = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Repeat = { Value = 2f },
            Mirror = { Value = true },
            ClipToBounds = { Value = true },
        };
        using PolarCoordinates.Resource p2rResource = p2rMirror.ToResource(CompositionContext.Default);
        (_, float[] p2rPixels) = FilterEffectTestHarness.RunOnTarget(p2rResource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(Channel(repeatPixels, outSize.Width, cx + dxSmall, cy - dy, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(repeatPixels, outSize.Width, cx - dxSmall, cy + dy, 0), Is.GreaterThan(0.95f), "non-mirrored 2nd cycle repeats from start");
            Assert.That(Channel(mirrorPixels, outSize.Width, cx + dxSmall, cy - dy, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(mirrorPixels, outSize.Width, cx - dxSmall, cy - dy, 0), Is.GreaterThan(0.95f), "mirrored 2nd cycle reflects across 12 o'clock");
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
        (float[] pixels, _, PixelSize outSize) = RunSolidWithBounds(halfArc, SKColors.White, s_size);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int offset = outSize.Width / 4;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, cx + offset, cy, 3), Is.GreaterThan(0.99f), "interior repeat seam inside arc stays opaque");
            Assert.That(Channel(pixels, outSize.Width, cx - offset, cy, 3), Is.LessThan(0.001f), "outside arc is transparent");
        });
    }

    [TestCase(PolarCoordinatesEdgeMode.Clamp)]
    [TestCase(PolarCoordinatesEdgeMode.Repeat)]
    [TestCase(PolarCoordinatesEdgeMode.Mirror)]
    public void NonTransparentEdgeModesFillOutsideTheRingAndBounds(PolarCoordinatesEdgeMode edgeMode)
    {
        var r2p = new PolarCoordinates
        {
            InnerRadius = { Value = 30f },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = edgeMode },
        };
        var p2r = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
            EdgeMode = { Value = edgeMode },
            ClipToBounds = { Value = true },
        };

        (float[] r2pPixels, _, PixelSize r2pSize) = RunSolidWithBounds(r2p, SKColors.White, s_size);
        (_, float[] p2rPixels) = FilterEffectTestHarness.RunOnSolidTarget(p2r, SKColors.White, s_size);

        int cx = r2pSize.Width / 2;
        int cy = r2pSize.Height / 2;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(r2pPixels, r2pSize.Width, cx, cy, 3), Is.GreaterThan(0.99f), $"inner hole filled ({edgeMode})");
            Assert.That(Channel(r2pPixels, r2pSize.Width, 0, 0, 3), Is.GreaterThan(0.99f), $"outer corner filled ({edgeMode})");
            Assert.That(Channel(r2pPixels, r2pSize.Width, cx - r2pSize.Width / 4, cy, 3), Is.GreaterThan(0.99f), $"outside arc filled ({edgeMode})");
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
        (float[] pixels, _, PixelSize outSize) = RunWithBounds(resource, s_size, DrawQuadrantStripes);

        int cx = outSize.Width / 2;
        int cy = outSize.Height / 2;
        int x = cx - outSize.Width / 6;
        int y = cy + outSize.Height / 4;
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, x, y, 0), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, outSize.Width, x, y, 1), Is.GreaterThan(0.95f));
            Assert.That(Channel(pixels, outSize.Width, x, y, 2), Is.GreaterThan(0.95f));
        });
    }

    [TestCase(PolarCoordinatesEdgeMode.Transparent)]
    [TestCase(PolarCoordinatesEdgeMode.Clamp)]
    public void DegenerateScaleAndSweepStayFinite(PolarCoordinatesEdgeMode edgeMode)
    {
        var effect = new PolarCoordinates
        {
            Scale = { Value = 0f },
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
    public void ExtendedParametersRoundTripBetweenDirectionsWithoutClipping()
    {
        const float stage1Inner = 30f;
        float stage1OuterR = (Width + Height + stage1Inner) / MathF.PI;
        float stage1InnerR = stage1OuterR * (stage1Inner / (Height + stage1Inner));

        var group = new FilterEffectGroup
        {
            Children =
            {
                new PolarCoordinates
                {
                    InnerRadius = { Value = stage1Inner },
                    Angle = { Value = 40f },
                    Swirl = { Value = 120f },
                    SweepAngle = { Value = 240f },
                    InvertRadius = { Value = true },
                    ReverseAngle = { Value = true },
                },
                new PolarCoordinates
                {
                    Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
                    InnerRadius = { Value = stage1InnerR },
                    Angle = { Value = 40f },
                    Swirl = { Value = 120f },
                    SweepAngle = { Value = 240f },
                    InvertRadius = { Value = true },
                    ReverseAngle = { Value = true },
                },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        (float[] before, _, _) = RunWithBounds(new FilterEffectGroup().ToResource(CompositionContext.Default), s_size, DrawGradient);
        (float[] after, Rect outBounds, PixelSize outSize) = RunWithBounds(resource, s_size, DrawGradient);

        float maximumError = 0f;
        for (int y = 10; y < outSize.Height - 10; y++)
        {
            int srcY = Math.Clamp((int)MathF.Round((y + 0.5f) / outSize.Height * Height - 0.5f), 0, Height - 1);
            for (int x = 16; x < outSize.Width - 16; x++)
            {
                int srcX = Math.Clamp((int)MathF.Round((x + 0.5f) / outSize.Width * Width - 0.5f), 0, Width - 1);
                for (int channel = 0; channel < 4; channel++)
                {
                    maximumError = MathF.Max(
                        maximumError,
                        MathF.Abs(Channel(after, outSize.Width, x, y, channel) - Channel(before, Width, srcX, srcY, channel)));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(outBounds.Height, Is.EqualTo(stage1OuterR - stage1InnerR).Within(0.05f), "round-trip matches radial band thickness");
            Assert.That(maximumError, Is.LessThan(0.04f));
        });
    }

    [Test]
    public void AmountBelowHundredUnionsInputAndPolarBounds()
    {
        var transparent = new PolarCoordinates
        {
            Amount = { Value = 50f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Transparent },
        };
        var mirrored = new PolarCoordinates
        {
            Amount = { Value = 50f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Mirror },
        };

        (float[] transparentPixels, Rect transparentBounds, PixelSize outSize) = RunSolidWithBounds(transparent, SKColors.White, s_size);
        (float[] mirroredPixels, Rect mirroredBounds, _) = RunSolidWithBounds(mirrored, SKColors.White, s_size);

        float expectedDiameter = ((Width + Height) / MathF.PI) * 2f;
        var wideBounds = new Rect(0, 0, 291, 28);
        (_, Rect wideUnionBounds) = FilterEffectTestHarness.RecordShader(transparent, wideBounds);
        float expectedWideDiameter = ((291f + 28f) / MathF.PI) * 2f;

        Assert.Multiple(() =>
        {
            Assert.That(transparentBounds.Width, Is.EqualTo(expectedDiameter).Within(0.05f));
            Assert.That(transparentBounds.Height, Is.EqualTo(expectedDiameter).Within(0.05f));
            Assert.That(mirroredBounds, Is.EqualTo(transparentBounds));
            Assert.That(wideUnionBounds.Width, Is.EqualTo(291f).Within(0.05f), "union preserves wide input width");
            Assert.That(wideUnionBounds.Height, Is.EqualTo(expectedWideDiameter).Within(0.05f), "union expands to polar diameter");
            // At the top inside row of the expanded frame (y=1 -> logical y~-25.5, mapped.y~0), Amount=50% places sample.y~-12.75 outside input [0, 81].
            Assert.That(Channel(transparentPixels, outSize.Width, outSize.Width / 2, 1, 3), Is.LessThan(0.001f), "transparent fades out-of-input samples");
            Assert.That(Channel(mirroredPixels, outSize.Width, outSize.Width / 2, 1, 3), Is.GreaterThan(0.99f), "mirror fills out-of-input samples");
        });
    }

    [Test]
    public void PolarToRectangularConvergesContinuouslyAsAmountReachesHundred()
    {
        var square = new PixelSize(100, 100);
        var squareBounds = new Rect(0, 0, 100, 100);
        var nearFull = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Amount = { Value = 99.99f },
        };
        var full = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Amount = { Value = 100f },
        };

        (ShaderDescription nearDesc, Rect nearBounds) = FilterEffectTestHarness.RecordShader(nearFull, squareBounds);
        (ShaderDescription fullDesc, Rect fullBounds) = FilterEffectTestHarness.RecordShader(full, squareBounds);

        using PolarCoordinates.Resource nearResource = nearFull.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource fullResource = full.ToResource(CompositionContext.Default);
        (float[] nearPixels, _, PixelSize nearSize) = RunWithBounds(nearResource, square, DrawGradient);
        (float[] fullPixels, _, PixelSize fullSize) = RunWithBounds(fullResource, square, DrawGradient);

        // Verify bounds, rendered interior pixels, and hit-test mapping all converge smoothly with no snap at 100%.
        var probe = new Point(fullBounds.X + fullBounds.Width * 0.25f, fullBounds.Y + fullBounds.Height * 0.6f);
        var accepted = new Rect(50f + 20f - 2f, 50f - 2f, 4f, 4f);
        var input = new RenderHitTestInput(squareBounds, accepted.ContainsExclusive);

        Assert.Multiple(() =>
        {
            Assert.That(nearBounds.X, Is.EqualTo(fullBounds.X).Within(0.02f));
            Assert.That(nearBounds.Y, Is.EqualTo(fullBounds.Y).Within(0.02f));
            Assert.That(nearBounds.Width, Is.EqualTo(fullBounds.Width).Within(0.02f));
            Assert.That(nearBounds.Height, Is.EqualTo(fullBounds.Height).Within(0.02f));
            Assert.That(Channel(nearPixels, nearSize.Width, nearSize.Width / 4, nearSize.Height / 2, 0),
                Is.EqualTo(Channel(fullPixels, fullSize.Width, fullSize.Width / 4, fullSize.Height / 2, 0)).Within(0.01f));
            Assert.That(fullDesc.HitTest!.Value.Evaluate(fullBounds, [input], [], probe), Is.True);
            Assert.That(nearDesc.HitTest!.Value.Evaluate(nearBounds, [input], [], probe), Is.True);
        });
    }

    [Test]
    public void ClipToBoundsKeepsOriginalInputBounds()
    {
        var effect = new PolarCoordinates
        {
            InnerRadius = { Value = 50f },
            Scale = { Value = 150f },
            Center = { Value = new RelativePoint(0.2f, 0.2f, RelativeUnit.Relative) },
            ClipToBounds = { Value = true },
        };
        (_, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);
        Assert.That(output, Is.EqualTo(s_bounds));
    }

    [Test]
    public void HitTestRespectsDonutHoleArcAndEdgeMode()
    {
        const float inner = 30f;
        var transparentDonut = new PolarCoordinates
        {
            InnerRadius = { Value = inner },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Transparent },
        };
        (ShaderDescription tDesc, Rect tOutput) = FilterEffectTestHarness.RecordShader(transparentDonut, s_bounds);
        RenderHitTestContract tHit = tDesc.HitTest!.Value;
        var fullInput = new RenderHitTestInput(s_bounds, s_bounds.ContainsExclusive);

        var clampedDonut = new PolarCoordinates
        {
            InnerRadius = { Value = inner },
            SweepAngle = { Value = 180f },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Clamp },
        };
        (ShaderDescription cDesc, Rect cOutput) = FilterEffectTestHarness.RecordShader(clampedDonut, s_bounds);
        RenderHitTestContract cHit = cDesc.HitTest!.Value;

        // s_bounds=(100x60), inner=30 -> rOuter=(100+60+30)/pi (~60.48), rInner=rOuter*30/90 (~20.16).
        float midR = (((s_bounds.Width + s_bounds.Height + inner) / MathF.PI) * (1f + inner / (s_bounds.Height + inner))) * 0.5f;
        var centerPoint = s_bounds.Center;
        var insideRingRight = new Point(s_bounds.Center.X + midR, s_bounds.Center.Y);
        var outsideArcLeft = new Point(s_bounds.Center.X - midR, s_bounds.Center.Y);

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
    public void HitTestRespectsCenterAngleSwirlAndExpandedOutputBounds()
    {
        var center = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);
        var rectToPolar = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.RectangularToPolar },
            Center = { Value = center },
            Angle = { Value = 90f },
            Swirl = { Value = 180f },
        };
        (ShaderDescription r2pDesc, Rect r2pOutput) = FilterEffectTestHarness.RecordShader(rectToPolar, s_bounds);
        RenderHitTestContract r2pHit = r2pDesc.HitTest!.Value;
        // Center is at local (25, 30), rOuter = (100 + 60) / pi (~50.93).
        // At 12 o'clock halfway out (vLinear = 0.5 -> v = 0.5):
        // angle = 0 deg, total offset = Angle(90) + Swirl(180 * (0.5 - 1)) = 0 deg -> u = 0 -> source local (0, 30).
        float expectedOuterR = (s_bounds.Width + s_bounds.Height) / MathF.PI;
        var r2pAccepted = new Rect(s_bounds.X - 1f, s_bounds.Center.Y - 1f, 2f, 2f);
        var r2pInput = new RenderHitTestInput(s_bounds, r2pAccepted.ContainsExclusive);
        var r2pPoint = new Point(s_bounds.X + 25f, s_bounds.Center.Y - expectedOuterR * 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(r2pOutput.Width, Is.EqualTo(expectedOuterR * 2f).Within(0.05f));
            Assert.That(r2pOutput.Height, Is.EqualTo(expectedOuterR * 2f).Within(0.05f));
            Assert.That(r2pHit.Evaluate(r2pOutput, [r2pInput], [], r2pPoint), Is.True);
            // Point past original left edge yet inside the expanded circle around the shifted center.
            Assert.That(r2pHit.Evaluate(r2pOutput, [new RenderHitTestInput(s_bounds, _ => true)], [], new Point(s_bounds.X - 20f, s_bounds.Center.Y)), Is.True);
            Assert.That(r2pHit.Evaluate(r2pOutput, [new RenderHitTestInput(s_bounds, _ => true)], [], new Point(s_bounds.X + 95f, s_bounds.Center.Y)), Is.False);
        });
    }

    [Test]
    public void HitTestClampsTranslatedPointInsideNonZeroOriginInputBounds()
    {
        var offsetBounds = new Rect(100f, 0f, 100f, 100f);
        var effect = new PolarCoordinates
        {
            Direction = { Value = PolarCoordinatesDirection.PolarToRectangular },
            Center = { Value = new RelativePoint(1f, 0.5f, RelativeUnit.Relative) },
            EdgeMode = { Value = PolarCoordinatesEdgeMode.Clamp },
            ClipToBounds = { Value = true },
        };
        (ShaderDescription desc, Rect output) = FilterEffectTestHarness.RecordShader(effect, offsetBounds);
        RenderHitTestContract hit = desc.HitTest!.Value;
        var input = new RenderHitTestInput(offsetBounds, offsetBounds.ContainsExclusive);

        // At u=0.25 (3 o'clock) and v=0.5, sample extends past the right edge (x=200) and clamps to it.
        // The clamped absolute hit point must remain strictly less than offsetBounds.Right (200).
        var probe = new Point(offsetBounds.X + 25f, offsetBounds.Center.Y);
        Assert.That(hit.Evaluate(output, [input], [], probe), Is.True);
    }

    [Test]
    public void RectangularToPolarConvergesContinuouslyAsAmountReachesHundred()
    {
        var nearFull = new PolarCoordinates { Amount = { Value = 99.99f }, ClipToBounds = { Value = true } };
        var full = new PolarCoordinates { Amount = { Value = 100f }, ClipToBounds = { Value = true } };
        using PolarCoordinates.Resource nearFullResource = nearFull.ToResource(CompositionContext.Default);
        using PolarCoordinates.Resource fullResource = full.ToResource(CompositionContext.Default);

        static void DrawHorizontalGradient(SKCanvas canvas)
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(Width, 0), [SKColors.Black, SKColors.White], [0f, 1f], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        }

        (_, float[] nearFullPixels) = FilterEffectTestHarness.RunOnTarget(nearFullResource, s_size, DrawHorizontalGradient);
        (_, float[] fullPixels) = FilterEffectTestHarness.RunOnTarget(fullResource, s_size, DrawHorizontalGradient);

        // Compressed seam pixel (64.5, 35.5) must converge smoothly instead of snapping when multisampling activates.
        float nearFullSeam = Channel(nearFullPixels, 64, 35, 0);
        float fullSeam = Channel(fullPixels, 64, 35, 0);
        float maxDiff = 0f;
        for (int i = 0; i < nearFullPixels.Length; i++)
            maxDiff = MathF.Max(maxDiff, MathF.Abs(nearFullPixels[i] - fullPixels[i]));

        Assert.Multiple(() =>
        {
            Assert.That(nearFullSeam, Is.EqualTo(fullSeam).Within(0.01f), "compressed seam converges at 100%");
            Assert.That(maxDiff, Is.LessThan(0.02f), "entire image converges continuously at 100%");
        });
    }

    [Test]
    public void RectangularToPolarPartialSweepFadesOutOfBoundsSamplesAtIntermediateAmount()
    {
        var size = new PixelSize(100, 100);
        var bounds = new Rect(0, 0, size.Width, size.Height);
        var effect = new PolarCoordinates
        {
            SweepAngle = { Value = 180f },
            Amount = { Value = 5f },
        };
        (float[] pixels, Rect outBounds, PixelSize outSize) = RunSolidWithBounds(effect, SKColors.White, size);
        (ShaderDescription desc, Rect recordedBounds) = FilterEffectTestHarness.RecordShader(effect, bounds);
        RenderHitTestContract hit = desc.HitTest!.Value;
        var input = new RenderHitTestInput(bounds, bounds.ContainsExclusive);

        // Logical point (-6.5, 70.5) has arcSampleX inside [0, 100] because uArc > 1,
        // while the actual sample.x (~-1.175) falls outside the source and must stay transparent.
        int px = (int)MathF.Floor(-6.5f) - (int)MathF.Floor(outBounds.X);
        int py = (int)MathF.Floor(70.5f) - (int)MathF.Floor(outBounds.Y);
        Assert.Multiple(() =>
        {
            Assert.That(px, Is.InRange(0, outSize.Width - 1));
            Assert.That(py, Is.InRange(0, outSize.Height - 1));
            Assert.That(Channel(pixels, outSize.Width, px, py, 3), Is.LessThan(0.001f), "out-of-bounds sample stays transparent");
            Assert.That(hit.Evaluate(recordedBounds, [input], [], new Point(-6.5f, 70.5f)), Is.False, "hit test rejects out-of-bounds sample");
        });
    }

    [TestCase(39f)]
    [TestCase(50f)]
    public void RectangularToPolarIntermediateAmountStaysBoundedWithinEndpointUnion(float amount)
    {
        var size = new PixelSize(100, 100);
        var bounds = new Rect(0, 0, size.Width, size.Height);
        var effect = new PolarCoordinates { Amount = { Value = amount } };
        (float[] pixels, Rect outBounds, PixelSize outSize) = RunSolidWithBounds(effect, SKColors.White, size);
        (ShaderDescription desc, _) = FilterEffectTestHarness.RecordShader(effect, bounds);
        RenderHitTestContract hit = desc.HitTest!.Value;
        var input = new RenderHitTestInput(bounds, bounds.ContainsExclusive);
        var enlargedBounds = new Rect(-100f, -100f, 400f, 400f);

        // Corners and off-axis perimeter pixels outside both inputBounds and the polar disc must be
        // transparent rather than hard-clipped at the output rectangle edge.
        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, outSize.Width, outSize.Width - 1, outSize.Height - 1, 3), Is.LessThan(0.001f), "bottom-right corner is transparent");
            Assert.That(Channel(pixels, outSize.Width, 0, outSize.Height - 1, 3), Is.LessThan(0.001f), "bottom-left corner is transparent");
            Assert.That(Channel(pixels, outSize.Width, outSize.Width - 1, (outSize.Height * 3) / 4, 3), Is.LessThan(0.001f), "right perimeter outside circle is transparent");
            Assert.That(Channel(pixels, outSize.Width, (outSize.Width * 3) / 4, outSize.Height - 1, 3), Is.LessThan(0.001f), "bottom perimeter outside circle is transparent");
            Assert.That(Channel(pixels, outSize.Width, outSize.Width / 2, outSize.Height / 2, 3), Is.GreaterThan(0.99f), "interior stays opaque");
            Assert.That(hit.Evaluate(enlargedBounds, [input], [], new Point(50f, 140f)), Is.False, "point past bottom circle rim is not hit");
            Assert.That(hit.Evaluate(enlargedBounds, [input], [], new Point(125f, 50f)), Is.False, "point past right circle rim is not hit");
            Assert.That(hit.Evaluate(outBounds, [input], [], new Point(50f, 50f)), Is.True, "center remains hit");
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

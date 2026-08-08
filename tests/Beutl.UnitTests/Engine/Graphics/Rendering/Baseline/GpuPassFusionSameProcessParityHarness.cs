using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

internal static class GpuPassFusionSameProcessParityHarness
{
    public const double MinimumSsim = 0.99;
    public const double MinimumWindowedSsim = 0.95;
    public const double MaximumLinearRgbMae = 0.02;
    public const double MaximumAlphaMae = 0.02;
    public const double MaximumAaEdgeChannelError = 0.02;
    public const double MaximumAaEdgeMeanError = 0.02;

    public static GpuPassFusionParityResult AssertParity(
        Func<FusionMode, Bitmap> render,
        PixelRect? aaEdgeRegion = null)
    {
        ArgumentNullException.ThrowIfNull(render);

        using Bitmap disabled = render(FusionMode.Disabled)
            ?? throw new InvalidOperationException("The fusion-disabled render returned null.");
        using Bitmap enabled = render(FusionMode.Enabled)
            ?? throw new InvalidOperationException("The fusion-enabled render returned null.");
        if (ReferenceEquals(disabled, enabled))
            throw new InvalidOperationException("Fusion-disabled and enabled runs must return independently owned images.");

        string? nonFinite = ImageMetrics.FirstNonFinite(
            ("fusion-disabled", disabled),
            ("fusion-enabled", enabled));
        Assert.That(nonFinite, Is.Null, "Same-process parity inputs must contain only finite RGBA16F values.");

        GpuPassFusionParityMetrics fullImage = Measure(disabled, enabled);
        GpuPassFusionAaParityMetrics? aaEdge = null;
        if (aaEdgeRegion is { } region)
        {
            ValidateCrop(region, disabled.Width, disabled.Height);
            using Bitmap disabledCrop = Crop(disabled, region);
            using Bitmap enabledCrop = Crop(enabled, region);
            GpuPassFusionParityMetrics cropMetrics = Measure(disabledCrop, enabledCrop);
            double edgeMeanError = ImageMetrics.EdgeBandMeanAbsoluteError(disabledCrop, enabledCrop);
            RgbaMaximumError edgeMaximum =
                ImageMetrics.EdgeBandMaximumAbsoluteErrorPerChannel(disabledCrop, enabledCrop);
            aaEdge = new GpuPassFusionAaParityMetrics(cropMetrics, edgeMeanError, edgeMaximum);
        }

        using (Assert.EnterMultipleScope())
        {
            AssertMetrics(fullImage, "full image");
            if (aaEdge is { } edge)
            {
                AssertMetrics(edge.Crop, "AA edge crop");
                Assert.That(
                    edge.EdgeBandMeanError,
                    Is.LessThanOrEqualTo(MaximumAaEdgeMeanError),
                    "AA edge-band mean error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Red,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge red-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Green,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge green-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Blue,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge blue-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Alpha,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge alpha-channel maximum error exceeded the fixed normal-CI bound.");
            }
        }

        return new GpuPassFusionParityResult(fullImage, aaEdge);
    }

    private static GpuPassFusionParityMetrics Measure(Bitmap disabled, Bitmap enabled)
    {
        return new GpuPassFusionParityMetrics(
            ImageMetrics.Ssim(disabled, enabled),
            ImageMetrics.WindowedSsim(disabled, enabled, 16),
            ImageMetrics.MeanAbsoluteError(disabled, enabled),
            ImageMetrics.AlphaMeanAbsoluteError(disabled, enabled));
    }

    private static void AssertMetrics(GpuPassFusionParityMetrics metrics, string region)
    {
        Assert.That(metrics.Ssim, Is.GreaterThanOrEqualTo(MinimumSsim), $"{region} SSIM was too low.");
        Assert.That(
            metrics.WindowedSsim,
            Is.GreaterThanOrEqualTo(MinimumWindowedSsim),
            $"{region} minimum-window SSIM was too low.");
        Assert.That(
            metrics.LinearRgbMae,
            Is.LessThanOrEqualTo(MaximumLinearRgbMae),
            $"{region} linear RGB MAE was too high.");
        Assert.That(
            metrics.AlphaMae,
            Is.LessThanOrEqualTo(MaximumAlphaMae),
            $"{region} alpha MAE was too high.");
    }

    private static Bitmap Crop(Bitmap source, PixelRect region)
    {
        var result = new Bitmap(
            region.Width,
            region.Height,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        for (int y = 0; y < region.Height; y++)
        {
            ReadOnlySpan<ushort> sourceRow = source.GetRow<ushort>(region.Y + y);
            Span<ushort> destinationRow = result.GetRow<ushort>(y);
            sourceRow.Slice(region.X * 4, region.Width * 4).CopyTo(destinationRow);
        }

        return result;
    }

    private static void ValidateCrop(PixelRect region, int width, int height)
    {
        if (region.X < 0
            || region.Y < 0
            || region.Width <= 0
            || region.Height <= 0
            || region.Right > width
            || region.Bottom > height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                $"AA edge region must be a non-empty subset of the {width}x{height} output.");
        }
    }
}

internal readonly record struct GpuPassFusionPixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public void ValidateInside(int imageWidth, int imageHeight, string description)
    {
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0 || Right > imageWidth || Bottom > imageHeight)
        {
            throw new InvalidDataException(
                $"{description} ({X}, {Y}, {Width}, {Height}) is not a non-empty subset of "
                + $"{imageWidth}x{imageHeight}.");
        }
    }
}

internal readonly record struct GpuPassFusionParityMetrics(
    double Ssim,
    double WindowedSsim,
    double LinearRgbMae,
    double AlphaMae);

internal readonly record struct GpuPassFusionAaParityMetrics(
    GpuPassFusionParityMetrics Crop,
    double EdgeBandMeanError,
    RgbaMaximumError MaximumError);

internal readonly record struct GpuPassFusionParityResult(
    GpuPassFusionParityMetrics FullImage,
    GpuPassFusionAaParityMetrics? AaEdge);

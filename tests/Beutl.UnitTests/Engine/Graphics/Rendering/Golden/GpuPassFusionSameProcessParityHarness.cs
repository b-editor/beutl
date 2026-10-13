using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;
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

    /// <param name="render">Renders the workload once per fusion mode, returning independently owned images.</param>
    /// <param name="aaEdgeRegion">The antialiased crop the edge-band bounds apply to, when the workload has one.</param>
    public static GpuPassFusionParityResult AssertParity(
        Func<FusionMode, Bitmap> render,
        PixelRect? aaEdgeRegion = null)
    {
        ArgumentNullException.ThrowIfNull(render);

        using Bitmap disabled = render(FusionMode.Disabled);
        using Bitmap enabled = render(FusionMode.Enabled);

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

        var result = new GpuPassFusionParityResult(fullImage, aaEdge);

        // Recorded before the assertions: an evidence run exists to say what the numbers were, and a manifest
        // that silently omits every workload that missed its threshold would report only the good news.
        if (GpuPassFusionParityEvidence.IsRecording)
        {
            GpuPassFusionParityEvidence.Record(
                TestContext.CurrentContext.Test.FullName,
                1.0,
                disabled.Width,
                disabled.Height,
                result,
                aaEdgeRegion);
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

        return result;
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

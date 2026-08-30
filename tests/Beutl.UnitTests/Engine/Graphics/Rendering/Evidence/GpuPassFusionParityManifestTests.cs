using System.Text.Json;

using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

/// <summary>
/// Pins the SC-007 artifact: that it records what was compared and at what scale, that it judges a case by the
/// harness's own thresholds, and that it does not quietly drop a workload that failed.
/// </summary>
[TestFixture]
public sealed class GpuPassFusionParityManifestTests
{
    [Test]
    public void Builder_RecordsTheComparedContentAndItsResult()
    {
        var builder = new GpuPassFusionParityManifestBuilder(
            GpuPassFusionParityManifest.SameProcessFusionMode);
        builder.SetFingerprint(PairedBenchmarkAnalyzerTests.TestFingerprint(), null);
        builder.Add(Case("Alpha", ssim: 0.999, outputScale: 1.0, width: 13, height: 9));
        builder.Add(Case("Beta", ssim: 0.995, outputScale: 2.0, width: 26, height: 18));

        GpuPassFusionParityManifest manifest = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(manifest.ComparisonMode, Is.EqualTo("same-process-fusion-disabled-vs-enabled"));
            Assert.That(manifest.CaseCount, Is.EqualTo(2));
            Assert.That(manifest.PassedCaseCount, Is.EqualTo(2));
            Assert.That(manifest.AllCasesPassed, Is.True);
            Assert.That(manifest.Cases["Beta"].OutputScale, Is.EqualTo(2.0));
            Assert.That(manifest.Cases["Beta"].Width, Is.EqualTo(26));
            Assert.That(manifest.EnvironmentFingerprint, Is.Not.Null);
            Assert.That(manifest.Thresholds.MinimumSsim,
                Is.EqualTo(GpuPassFusionSameProcessParityHarness.MinimumSsim));
            Assert.That(manifest.Thresholds.MaximumAaEdgeChannelError, Is.EqualTo(0.02));
        });
    }

    [Test]
    public void Builder_ReportsAnEmptyRunAsNotPassing()
    {
        GpuPassFusionParityManifest manifest =
            new GpuPassFusionParityManifestBuilder(GpuPassFusionParityManifest.SameProcessFusionMode).Build();
        Assert.Multiple(() =>
        {
            Assert.That(manifest.CaseCount, Is.Zero);
            Assert.That(manifest.AllCasesPassed, Is.False,
                "an artifact with nothing in it must not read as a clean sweep");
            Assert.That(manifest.EnvironmentFingerprint, Is.Null);
        });
    }

    [Test]
    public void Builder_KeepsTheWorstResultWhenACaseIsComparedTwice()
    {
        var builder = new GpuPassFusionParityManifestBuilder(
            GpuPassFusionParityManifest.SameProcessFusionMode);
        builder.Add(Case("Alpha", ssim: 0.9995));
        builder.Add(Case("Alpha", ssim: 0.9910));
        builder.Add(Case("Alpha", ssim: 0.9999));

        Assert.That(builder.Build().Cases["Alpha"].Ssim, Is.EqualTo(0.9910));
    }

    [Test]
    public void Builder_KeepsAFailureOverALaterPass()
    {
        var builder = new GpuPassFusionParityManifestBuilder(
            GpuPassFusionParityManifest.SameProcessFusionMode);
        builder.Add(Case("Alpha", ssim: 0.5));
        builder.Add(Case("Alpha", ssim: 0.9999));

        GpuPassFusionParityManifest manifest = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(manifest.Cases["Alpha"].Passed, Is.False);
            Assert.That(manifest.AllCasesPassed, Is.False);
            Assert.That(manifest.PassedCaseCount, Is.Zero);
        });
    }

    [Test]
    public void CreateCase_FailsAWorkloadThatMissedAThreshold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Case("Low", ssim: 0.98).Passed, Is.False, "SSIM below 0.99");
            Assert.That(Case("Ok", ssim: 0.99).Passed, Is.True, "SSIM exactly at the bound");
        });
    }

    [Test]
    public void CreateCase_AppliesTheEdgeBandBoundsWhenAWorkloadDeclaresACrop()
    {
        var region = new PixelRect(18, 16, 156, 76);
        GpuPassFusionParityCase within = GpuPassFusionParityManifestBuilder.CreateCase(
            "Aa", 1.0, 200, 120, Result(0.999, edgeMaximum: 0.01), region);
        GpuPassFusionParityCase beyond = GpuPassFusionParityManifestBuilder.CreateCase(
            "Aa", 1.0, 200, 120, Result(0.999, edgeMaximum: 0.03), region);

        Assert.Multiple(() =>
        {
            Assert.That(within.Passed, Is.True);
            Assert.That(beyond.Passed, Is.False, "a per-channel edge error above 0.02 is not parity");
            Assert.That(within.AaEdgeRegion, Is.EqualTo("18, 16, 156, 76"));
            Assert.That(within.AaEdgeMaximumRedError, Is.EqualTo(0.01));
            Assert.That(beyond.AaEdgeBandMeanError, Is.EqualTo(0.001));
        });
    }

    [Test]
    public void Manifest_SerializesWithTheFieldsAReaderNeedsToJudgeIt()
    {
        var builder = new GpuPassFusionParityManifestBuilder(
            GpuPassFusionParityManifest.SameProcessFusionMode);
        builder.SetFingerprint(PairedBenchmarkAnalyzerTests.TestFingerprint(), null);
        builder.Add(Case("Alpha", ssim: 0.999));

        using JsonDocument document = JsonDocument.Parse(builder.Build().ToJson());
        JsonElement root = document.RootElement;
        Assert.Multiple(() =>
        {
            foreach (string name in new[]
                     {
                         "schemaVersion", "generatedAtUtc", "comparisonMode", "beutlEngineSourceRevision",
                         "beutlEngineAssemblyVersion", "thresholds", "environmentFingerprint", "caseCount",
                         "passedCaseCount", "allCasesPassed", "cases",
                     })
            {
                Assert.That(root.TryGetProperty(name, out _), Is.True, $"manifest must record '{name}'");
            }

            Assert.That(
                root.GetProperty("environmentFingerprint").GetProperty("maxAttachmentDimension").GetInt32(),
                Is.EqualTo(16384));
        });
    }

    private static GpuPassFusionParityCase Case(
        string name,
        double ssim,
        double outputScale = 1.0,
        int width = 13,
        int height = 9)
        => GpuPassFusionParityManifestBuilder.CreateCase(name, outputScale, width, height, Result(ssim), null);

    private static GpuPassFusionParityResult Result(double ssim, double? edgeMaximum = null)
    {
        var metrics = new GpuPassFusionParityMetrics(ssim, ssim, 0.001, 0.001);
        GpuPassFusionAaParityMetrics? edge = edgeMaximum is { } maximum
            ? new GpuPassFusionAaParityMetrics(metrics, 0.001, new RgbaMaximumError(maximum, maximum, maximum, maximum))
            : null;
        return new GpuPassFusionParityResult(metrics, edge);
    }
}

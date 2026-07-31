using Beutl.Benchmarks.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

using System.Text.Json;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

[NonParallelizable]
[TestFixture]
public sealed class GpuPassFusionFrozenBaselineLiveTests
{
    private static readonly Lazy<GpuPassFusionEvidenceManifest> s_manifest =
        new(GpuPassFusionBaselineEvidence.LoadAndVerify);

    private static IEnumerable<TestCaseData> Workloads()
    {
        foreach (GpuPassFusionEvidenceScene scene in s_manifest.Value.Scenes.Where(scene => scene.Blob is not null))
        {
            yield return new TestCaseData(scene.Id)
                .SetName($"FrozenBaseline_LiveRenderMatchesReference({scene.Id})");
        }
    }

    [TestCaseSource(nameof(Workloads))]
    public void FrozenBaseline_LiveRenderMatchesReference(string sceneId)
    {
        var graphicsContext = VulkanTestEnvironment.EnsureAvailable();
        GpuPassFusionEvidenceManifest manifest = s_manifest.Value;
        RenderPipelineEvidenceFingerprint currentFingerprint = VulkanTestEnvironment.InvokeOnRenderThread(
            () => RenderPipelineEvidenceFingerprint.Capture(graphicsContext));
        using JsonDocument currentFingerprintDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(
                currentFingerprint,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        // Boot-scoped identifiers (IOKit registry ids and the UUIDs MoltenVK derives from
        // them) change across reboots of the same machine; gating on them would skip the
        // authoritative host too. Code provenance is checked by the paired pipeline instead.
        string[] volatileFingerprintFields =
        [
            "beutlEngineAssemblyVersion",
            "metalRegistryId",
            "vulkanDeviceUuid",
            "vulkanDriverUuid",
        ];
        string[] fingerprintMismatches = manifest.Fingerprint.Keys
            .Where(name => !volatileFingerprintFields.Contains(name, StringComparer.Ordinal))
            .Where(name => !FingerprintValues(currentFingerprintDocument.RootElement.GetProperty(name))
                .SequenceEqual(manifest.Fingerprint[name], StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (fingerprintMismatches.Length != 0)
        {
            Assert.Ignore(
                "The current environment differs from the frozen visual-baseline fingerprint: "
                + string.Join(", ", fingerprintMismatches));
        }

        if (sceneId.StartsWith("scene3d-", StringComparison.Ordinal)
            && !graphicsContext.Supports3DRendering)
        {
            Assert.Ignore("The selected Vulkan device does not support the manifest's Scene3D workloads.");
        }

        GpuPassFusionEvidenceScene scene = manifest.GetScene(sceneId);
        PixelRect? requestedRegion = scene.RequestedRegion is { } requested
            ? new PixelRect(requested.X, requested.Y, requested.Width, requested.Height)
            : null;
        FeatureVisualCapture live = VulkanTestEnvironment.InvokeOnRenderThread(
            () => FeatureVisualEvidenceExporter.CaptureVisualSceneForTest(
                scene.Id,
                scene.OutputScale,
                scene.MaxWorkingScale,
                requestedRegion));
        byte[] reference = File.ReadAllBytes(
            Path.Combine(manifest.Paths.BaselineDirectory, scene.Blob!));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(live.Width, Is.EqualTo(scene.BlobWidth), "live width differs from the manifest");
            Assert.That(live.Height, Is.EqualTo(scene.BlobHeight), "live height differs from the manifest");
            Assert.That(
                live.Bytes.Length,
                Is.EqualTo(reference.Length),
                "live RGBA16F payload length differs from the frozen blob");
        }

        Rgba16fParityMetrics full = Rgba16fEvidenceWriter.CalculateParity(
            reference,
            live.Bytes,
            scene.BlobWidth,
            scene.BlobHeight,
            region: null);
        TestContext.WriteLine(
            $"{scene.Id}: SSIM={full.LinearLightSsim:F9}, "
            + $"linearRgbMae={full.LinearRgbMae:F9}, alphaMae={full.AlphaMae:F9}");
        AssertParity(full, scene.Id, "full image");

        if (scene.EdgeCrop is not { } crop)
            return;

        crop.ValidateInside(scene.BlobWidth, scene.BlobHeight, $"{scene.Id} edge crop");
        var pixelCrop = new PixelRect(crop.X, crop.Y, crop.Width, crop.Height);
        Rgba16fParityMetrics cropped = Rgba16fEvidenceWriter.CalculateParity(
            reference,
            live.Bytes,
            scene.BlobWidth,
            scene.BlobHeight,
            pixelCrop);
        Rgba16fCoverageBandMetrics coverage = Rgba16fEvidenceWriter.CalculateCoverageBand(
            reference,
            live.Bytes,
            scene.BlobWidth,
            scene.BlobHeight,
            pixelCrop);
        TestContext.WriteLine(
            $"{scene.Id} edge: SSIM={cropped.LinearLightSsim:F9}, "
            + $"linearRgbMae={cropped.LinearRgbMae:F9}, alphaMae={cropped.AlphaMae:F9}, "
            + $"coverageRgbaMae={coverage.RgbaMae:F9}, coverageMax={coverage.MaximumError.Maximum:F9}");
        AssertParity(cropped, scene.Id, "AA edge crop");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                coverage.RgbaMae,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance),
                $"{scene.Id} AA coverage-band mean exceeded the manifest tolerance");
            Assert.That(
                coverage.MaximumError.Maximum,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance),
                $"{scene.Id} AA coverage-band maximum exceeded the manifest tolerance");
        }
    }

    [Test]
    public void Scene3dWith2dTail_UsesProductionFilterBoundary()
    {
        var graphicsContext = VulkanTestEnvironment.EnsureAvailable();
        if (!graphicsContext.Supports3DRendering)
            Assert.Ignore("The selected Vulkan device does not support Scene3D rendering.");

        GpuPassFusionEvidenceScene scene = s_manifest.Value.GetScene("scene3d-with-2d-tail");
        FeatureVisualCapture live = VulkanTestEnvironment.InvokeOnRenderThread(
            () => FeatureVisualEvidenceExporter.CaptureVisualSceneForTest(
                scene.Id,
                scene.OutputScale,
                scene.MaxWorkingScale,
                requestedRegion: null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(live.Width, Is.EqualTo(scene.BlobWidth));
            Assert.That(live.Height, Is.EqualTo(scene.BlobHeight));
            Assert.That(
                live.Bytes,
                Has.Some.Not.Zero,
                "The production FilterEffectRenderNode boundary must materialize the Scene3D surface for its 2D tail.");
        }
    }

    private static IEnumerable<string> FingerprintValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Select(static item => item.GetString()!);

        return [element.GetString()!];
    }

    private static void AssertParity(Rgba16fParityMetrics metrics, string sceneId, string region)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                metrics.LinearLightSsim,
                Is.GreaterThanOrEqualTo(0.99),
                $"{sceneId} {region} SSIM is below the frozen-baseline contract");
            Assert.That(
                metrics.LinearRgbMae,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance),
                $"{sceneId} {region} linear RGB MAE exceeded the manifest tolerance");
            Assert.That(
                metrics.AlphaMae,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance),
                $"{sceneId} {region} alpha MAE exceeded the manifest tolerance");
        }
    }
}

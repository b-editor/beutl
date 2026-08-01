using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Benchmarks.Rendering;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

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
        double minimumWindowedSsim = CalculateMinimumWindowedSsim(
            reference,
            live.Bytes,
            scene.BlobWidth,
            scene.BlobHeight);
        TestContext.WriteLine(
            $"{scene.Id}: SSIM={full.LinearLightSsim:F9}, "
            + $"minimumWindowedSsim={minimumWindowedSsim:F9}, "
            + $"linearRgbMae={full.LinearRgbMae:F9}, alphaMae={full.AlphaMae:F9}");
        AssertParity(full, minimumWindowedSsim, scene.Id, "full image");

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
        AssertParity(cropped, minimumWindowedSsim: null, scene.Id, "AA edge crop");
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
    public void LiveParityGate_RejectsLocalizedDefectThatPassesWholeFrameMetrics()
    {
        const int size = 128;
        byte[] reference = CreateCheckerboardPayload(size, withLocalizedDefect: false);
        byte[] actual = CreateCheckerboardPayload(size, withLocalizedDefect: true);
        Rgba16fParityMetrics full = Rgba16fEvidenceWriter.CalculateParity(
            reference,
            actual,
            size,
            size,
            region: null);
        double windowed = CalculateMinimumWindowedSsim(reference, actual, size, size);

        Assert.Multiple(() =>
        {
            Assert.That(full.LinearLightSsim, Is.GreaterThanOrEqualTo(0.99));
            Assert.That(
                full.LinearRgbMae,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance));
            Assert.That(
                full.AlphaMae,
                Is.LessThanOrEqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance));
            Assert.That(windowed, Is.LessThan(GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim));
        });
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

    [Test]
    public void AllocationFailureCapture_PinsPreviewAndDeliveryEvidence()
    {
        VulkanTestEnvironment.EnsureAvailable();
        JsonArray captures = VulkanTestEnvironment.InvokeOnRenderThread(
            FeatureVisualEvidenceExporter.CaptureAllocationFailuresForTest);
        TestContext.WriteLine(captures.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Assert.That(captures, Has.Count.EqualTo(2));
        JsonObject preview = captures[0]?.AsObject()
            ?? throw new AssertionException("The preview allocation capture is missing.");
        JsonObject delivery = captures[1]?.AsObject()
            ?? throw new AssertionException("The delivery allocation capture is missing.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview["intent"]?.GetValue<string>(), Is.EqualTo("preview"));
            Assert.That(
                preview["injectionPoint"]?.GetValue<string>(),
                Is.EqualTo("next EffectMaterialization RenderTarget.Create"));
            Assert.That(preview["maxWorkingScale"]?.GetValue<string>(), Is.EqualTo("2"));
            Assert.That(preview["outcome"]?.GetValue<string>(), Is.EqualTo("dropped-output-without-throw"));
            Assert.That(preview["exceptionType"], Is.Null);
            Assert.That(preview["exceptionMessage"], Is.Null);
            Assert.That(preview["requestSucceeded"]?.GetValue<bool>(), Is.True);
            Assert.That(preview["outputBounds"]?.GetValue<string>(), Is.EqualTo("5,3,182,102"));
            Assert.That(preview["outputScale"]?.GetValue<float>(), Is.EqualTo(1));
            Assert.That(preview["outputIsEmpty"]?.GetValue<bool>(), Is.False);
            Assert.That(preview["outputBitmapWidth"]?.GetValue<int>(), Is.EqualTo(182));
            Assert.That(preview["outputBitmapHeight"]?.GetValue<int>(), Is.EqualTo(102));
            Assert.That(preview["outputNonZeroComponents"]?.GetValue<int>(), Is.Zero);
            Assert.That(preview["outputNonFiniteComponents"]?.GetValue<int>(), Is.Zero);
            Assert.That(
                preview["outputSha256"]?.GetValue<string>(),
                Is.EqualTo("5e364eb2f6cc38287f3aec69da9cd156c1d7e6653a5083a7889cdf204d983fba"));
            Assert.That(preview["targetFactoryCreateCalls"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(preview["failedAllocationWidth"]?.GetValue<int>(), Is.EqualTo(174));
            Assert.That(preview["failedAllocationHeight"]?.GetValue<int>(), Is.EqualTo(94));
            AssertCounters(
                preview,
                new Dictionary<string, long>
                {
                    ["ExecutionIslands"] = 2,
                    ["ExternalRootResources"] = 1,
                    ["OpaqueBoundaries"] = 2,
                    ["PlannedGpuPasses"] = 1,
                    ["PoolMisses"] = 1,
                    ["PreviewAllocationDrops"] = 1,
                    ["RecordedFragments"] = 2,
                    ["RecordedMaterializableValues"] = 2,
                    ["RenderCacheResolutionPasses"] = 1,
                    ["SkippedOutcomes"] = 2,
                    ["StructuralPlanCompilations"] = 1,
                    ["StructuralPlanMisses"] = 1,
                });

            Assert.That(delivery["intent"]?.GetValue<string>(), Is.EqualTo("delivery"));
            Assert.That(
                delivery["injectionPoint"]?.GetValue<string>(),
                Is.EqualTo("next EffectMaterialization RenderTarget.Create"));
            Assert.That(delivery["maxWorkingScale"]?.GetValue<string>(), Is.EqualTo("+Infinity"));
            Assert.That(delivery["outcome"]?.GetValue<string>(), Is.EqualTo("threw"));
            Assert.That(
                delivery["exceptionType"]?.GetValue<string>(),
                Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(
                delivery["exceptionMessage"]?.GetValue<string>(),
                Is.EqualTo("The render-target factory could not allocate 174x94 pixels."));
            Assert.That(delivery["requestSucceeded"]?.GetValue<bool>(), Is.False);
            Assert.That(delivery["outputBounds"], Is.Null);
            Assert.That(delivery["outputScale"], Is.Null);
            Assert.That(delivery["outputIsEmpty"], Is.Null);
            Assert.That(delivery["outputBitmapWidth"], Is.Null);
            Assert.That(delivery["outputBitmapHeight"], Is.Null);
            Assert.That(delivery["outputNonZeroComponents"], Is.Null);
            Assert.That(delivery["outputNonFiniteComponents"], Is.Null);
            Assert.That(delivery["outputSha256"], Is.Null);
            Assert.That(delivery["targetFactoryCreateCalls"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(delivery["failedAllocationWidth"]?.GetValue<int>(), Is.EqualTo(174));
            Assert.That(delivery["failedAllocationHeight"]?.GetValue<int>(), Is.EqualTo(94));
            AssertCounters(
                delivery,
                new Dictionary<string, long>
                {
                    ["ExecutionIslands"] = 2,
                    ["ExternalRootResources"] = 1,
                    ["FailedOutcomes"] = 1,
                    ["Failures"] = 1,
                    ["OpaqueBoundaries"] = 2,
                    ["PlannedGpuPasses"] = 1,
                    ["PoolMisses"] = 1,
                    ["RecordedFragments"] = 2,
                    ["RecordedMaterializableValues"] = 2,
                    ["RenderCacheResolutionPasses"] = 1,
                    ["SkippedOutcomes"] = 1,
                    ["StructuralPlanCompilations"] = 1,
                    ["StructuralPlanMisses"] = 1,
                });
        }
    }

    private static void AssertCounters(JsonObject capture, IReadOnlyDictionary<string, long> expected)
    {
        JsonObject actual = capture["featureCounters"]?.AsObject()
            ?? throw new AssertionException("The allocation capture has no feature counters.");
        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        foreach ((string name, long value) in expected)
            Assert.That(actual[name]?.GetValue<long>(), Is.EqualTo(value), $"counter {name}");
    }

    private static IEnumerable<string> FingerprintValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Select(static item => item.GetString()!);

        return [element.GetString()!];
    }

    private static double CalculateMinimumWindowedSsim(
        byte[] reference,
        byte[] actual,
        int width,
        int height)
    {
        const int windowSize = 16;
        double minimum = 1;
        for (int top = 0; top < height; top += windowSize)
        {
            for (int left = 0; left < width; left += windowSize)
            {
                var window = new PixelRect(
                    left,
                    top,
                    Math.Min(windowSize, width - left),
                    Math.Min(windowSize, height - top));
                Rgba16fParityMetrics metrics = Rgba16fEvidenceWriter.CalculateParity(
                    reference,
                    actual,
                    width,
                    height,
                    window);
                minimum = Math.Min(minimum, metrics.LinearLightSsim);
            }
        }

        return minimum;
    }

    private static byte[] CreateCheckerboardPayload(int size, bool withLocalizedDefect)
    {
        var result = new byte[checked(size * size * 8)];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = withLocalizedDefect && x < 14 && y < 14
                    ? 0.5f
                    : (x + y) % 2;
                int offset = ((y * size) + x) * 8;
                WriteHalf(result, offset, value);
                WriteHalf(result, offset + 2, value);
                WriteHalf(result, offset + 4, value);
                WriteHalf(result, offset + 6, 1);
            }
        }

        return result;
    }

    private static void WriteHalf(byte[] destination, int offset, float value)
        => BinaryPrimitives.WriteUInt16LittleEndian(
            destination.AsSpan(offset, sizeof(ushort)),
            BitConverter.HalfToUInt16Bits((Half)value));

    private static void AssertParity(
        Rgba16fParityMetrics metrics,
        double? minimumWindowedSsim,
        string sceneId,
        string region)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                metrics.LinearLightSsim,
                Is.GreaterThanOrEqualTo(0.99),
                $"{sceneId} {region} SSIM is below the frozen-baseline contract");
            if (minimumWindowedSsim is { } windowedSsim)
            {
                Assert.That(
                    windowedSsim,
                    Is.GreaterThanOrEqualTo(GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim),
                    $"{sceneId} {region} minimum-window SSIM is below the frozen-baseline contract");
            }
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

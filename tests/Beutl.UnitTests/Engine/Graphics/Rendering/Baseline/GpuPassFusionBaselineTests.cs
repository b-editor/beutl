using System.Reflection;
using System.Text.Json;

using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

[TestFixture]
public sealed class GpuPassFusionBaselineTests
{
    private const string ExpectedTargetBenchmarkManifestSha256 =
        "39272c4eef6414cf4099f3f2ab0a388dded36473e86deb506f127c65b85e78c6";

    private const string ExpectedPairedBenchmarkManifestSha256 =
        "839eaf34e4fa5824a03333fa50418259ea3fca302a044eb767110afb6b676b1e";

    private static readonly string[] ExpectedPairedBenchmarkCounterFiles =
    [
        "LongInvariantChain.json",
        "MixedSpatialColor.json",
        "MultipleDrawablesTargetDependencies.json",
        "NoEffectControl.json",
        "ParameterOnlyAnimation.json",
        "ShaderOpacityShader.json",
        "ShaderOpacityShaderBarrier.json",
        "SingleShader.json",
        "SmallObjectFixedOverhead.json",
        "StaticPrefixAnimatedTail.json",
        "StructuralToggle.json",
    ];

    private static readonly string[] ExpectedPairedBenchmarkArchiveFiles =
    [
        .. ExpectedPairedBenchmarkCounterFiles.Select(file => $"baseline-a/counters/{file}"),
        .. ExpectedPairedBenchmarkCounterFiles.Select(file => $"baseline-b/counters/{file}"),
        .. ExpectedPairedBenchmarkCounterFiles.Select(file => $"feature/counters/{file}"),
        "baseline-a/code-sha.txt",
        "baseline-a/command.txt",
        "baseline-a/raw-benchmark-full.json",
        "baseline-a/raw-benchmark-stdout.txt",
        "baseline-b/code-sha.txt",
        "baseline-b/command.txt",
        "baseline-b/raw-benchmark-full.json",
        "baseline-b/raw-benchmark-stdout.txt",
        "feature/code-sha.txt",
        "feature/command.txt",
        "feature/raw-benchmark-full.json",
        "feature/raw-benchmark-stdout.txt",
        "manifest.json",
    ];

    [Test]
    public void IntentionalRefreshScript_StagesAllLinkedTrustAnchorsBeforePublishing()
    {
        GpuPassFusionEvidencePaths paths = GpuPassFusionEvidencePaths.Discover();
        string script = File.ReadAllText(paths.RefreshScriptPath);
        string benchmarkTest = File.ReadAllText(Path.Combine(
            paths.RepositoryRoot,
            "tests",
            "Beutl.UnitTests",
            "Engine",
            "Graphics",
            "Rendering",
            "Baseline",
            "GpuPassFusionBaselineTests.cs"));
        string acceptanceReport = File.ReadAllText(Path.Combine(
            paths.EvidenceDirectory,
            "acceptance-report.md"));
        int stagingStart = script.IndexOf("staged = []", StringComparison.Ordinal);
        int benchmarkStage = script.IndexOf(
            "staged.append((benchmark_test_stage, benchmark_test))",
            StringComparison.Ordinal);
        int acceptanceStage = script.IndexOf(
            "staged.append((acceptance_stage, acceptance_report))",
            StringComparison.Ordinal);
        int publishStart = script.IndexOf("for source, destination in staged:", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(
                System.Text.RegularExpressions.Regex.Matches(
                    benchmarkTest,
                    "ExpectedTargetBenchmarkManifestSha256\\s*=\\s*\"[0-9a-f]{64}\";").Count,
                Is.EqualTo(1));
            Assert.That(
                System.Text.RegularExpressions.Regex.Matches(
                    acceptanceReport,
                    "The current immutable trust-chain anchors are target visual manifest\\s+"
                    + "`[0-9a-f]{64}` and\\s+target benchmark manifest\\s+`[0-9a-f]{64}`\\.").Count,
                Is.EqualTo(1));
            Assert.That(script, Does.Contain("benchmark manifest test trust anchor"));
            Assert.That(script, Does.Contain("acceptance visual manifest trust anchor"));
            Assert.That(script, Does.Contain("acceptance benchmark manifest trust anchor"));
            Assert.That(stagingStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(benchmarkStage, Is.GreaterThan(stagingStart).And.LessThan(publishStart));
            Assert.That(acceptanceStage, Is.GreaterThan(stagingStart).And.LessThan(publishStart));
        });
    }

    [Test]
    public void ImmutableEvidence_HasPinnedManifestToolAndBlobIntegrity()
    {
        GpuPassFusionEvidenceManifest manifest = GpuPassFusionBaselineEvidence.LoadAndVerify();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.SchemaVersion, Is.EqualTo(GpuPassFusionBaselineEvidence.ExpectedSchemaVersion));
            Assert.That(
                manifest.BaselineCodeSha,
                Is.EqualTo(GpuPassFusionBaselineEvidence.ExpectedBaselineCodeSha));
            Assert.That(manifest.GeneratorSeed, Is.EqualTo(GpuPassFusionBaselineEvidence.ExpectedGeneratorSeed));
            Assert.That(manifest.ArtifactHashes, Is.Not.Empty);
            Assert.That(manifest.Scenes.Count(scene => scene.Role == "parity"), Is.GreaterThan(0));
            Assert.That(
                manifest.Fingerprint.Keys,
                Is.EquivalentTo(GpuPassFusionBaselineEvidence.RequiredFingerprintFields));
        }
    }

    [Test]
    public void ImmutableTargetBenchmark_HasPinnedSchemaProvenanceFileSetAndArtifactHashes()
    {
        GpuPassFusionEvidenceManifest baseline = GpuPassFusionBaselineEvidence.LoadAndVerify();
        string directory = Path.Combine(baseline.Paths.EvidenceDirectory, "target-benchmark");
        string manifestPath = Path.Combine(directory, "manifest.json");
        GpuPassFusionBaselineEvidence.VerifyFileHash(
            manifestPath,
            ExpectedTargetBenchmarkManifestSha256,
            "target benchmark manifest");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        Assert.That(
            root.EnumerateObject().Select(static property => property.Name),
            Is.EquivalentTo(new[]
            {
                "artifactSha256",
                "baselineCodeSha",
                "benchmarkDotNetVersion",
                "cases",
                "command",
                "configuration",
                "evidenceTools",
                "fingerprint",
                "patchedDiffSha256",
                "prePatchRepositoryState",
                "runCompletedUtc",
                "runStartedUtc",
                "schemaVersion",
                "scope",
                "visualManifestSha256",
            }));
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("baselineCodeSha").GetString(), Is.EqualTo(baseline.BaselineCodeSha));
            Assert.That(root.GetProperty("patchedDiffSha256").GetString(), Is.EqualTo(baseline.PatchedDiffSha256));
            Assert.That(root.GetProperty("prePatchRepositoryState").GetString(), Is.EqualTo("clean"));
            Assert.That(
                root.GetProperty("visualManifestSha256").GetString(),
                Is.EqualTo(GpuPassFusionBaselineEvidence.ExpectedManifestSha256));
        });

        IReadOnlyDictionary<string, string> expectedTools = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["benchmarkRunnerSha256"] = baseline.EvidenceTools.BenchmarkRunnerSha256,
            ["generatorPatchSha256"] = baseline.EvidenceTools.GeneratorPatchSha256,
            ["generatorScriptSha256"] = baseline.EvidenceTools.GeneratorScriptSha256,
            ["generatorSourceBundleSha256"] = baseline.EvidenceTools.GeneratorSourceBundleSha256,
            ["pairedRunnerSha256"] = baseline.EvidenceTools.PairedRunnerSha256,
            ["refreshScriptSha256"] = baseline.EvidenceTools.RefreshScriptSha256,
        };
        JsonElement tools = root.GetProperty("evidenceTools");
        Assert.That(
            tools.EnumerateObject().Select(static property => property.Name),
            Is.EquivalentTo(expectedTools.Keys));
        foreach ((string name, string expected) in expectedTools)
            Assert.That(tools.GetProperty(name).GetString(), Is.EqualTo(expected), name);

        JsonElement fingerprint = root.GetProperty("fingerprint");
        Assert.That(
            fingerprint.EnumerateObject().Select(static property => property.Name),
            Is.EquivalentTo(baseline.Fingerprint.Keys));
        foreach ((string name, IReadOnlyList<string> expected) in baseline.Fingerprint)
        {
            JsonElement actual = fingerprint.GetProperty(name);
            IReadOnlyList<string?> values = actual.ValueKind == JsonValueKind.Array
                ? actual.EnumerateArray().Select(static item => item.GetString()).ToArray()
                : [actual.GetString()];
            Assert.That(values, Is.EqualTo(expected), name);
        }

        string[] expectedArtifacts =
        [
            "counters.json",
            "raw-benchmark-full.json",
            "raw-benchmark-github.md",
            "raw-benchmark-stdout.txt",
        ];
        JsonElement artifactHashes = root.GetProperty("artifactSha256");
        Assert.That(
            artifactHashes.EnumerateObject().Select(static property => property.Name),
            Is.EquivalentTo(expectedArtifacts));
        string[] actualFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(actualFiles, Is.EqualTo(expectedArtifacts.Append("manifest.json").Order(StringComparer.Ordinal)));
        foreach (string artifact in expectedArtifacts)
        {
            GpuPassFusionBaselineEvidence.VerifyFileHash(
                Path.Combine(directory, artifact),
                artifactHashes.GetProperty(artifact).GetString()
                    ?? throw new InvalidDataException($"Target benchmark hash is null: {artifact}"),
                $"target benchmark artifact '{artifact}'");
        }
    }

    [Test]
    public void PairedBenchmarkArchive_HasPinnedManifestFileSetAndArtifactHashes()
    {
        GpuPassFusionEvidencePaths paths = GpuPassFusionEvidencePaths.Discover();
        string directory = Path.Combine(paths.EvidenceDirectory, "paired-benchmark-run");
        string manifestPath = Path.Combine(directory, "manifest.json");
        GpuPassFusionBaselineEvidence.VerifyFileHash(
            manifestPath,
            ExpectedPairedBenchmarkManifestSha256,
            "paired benchmark manifest");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(
                root.EnumerateObject().Select(static property => property.Name),
                Does.Contain("overallAcceptancePassed"));
        });

        string[] actualFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(actualFiles, Is.EqualTo(ExpectedPairedBenchmarkArchiveFiles.Order(StringComparer.Ordinal)));

        foreach (string laneName in new[] { "baseline", "baselineRepeat", "feature" })
        {
            JsonElement lane = root.GetProperty(laneName);
            string laneDirectory = laneName switch
            {
                "baseline" => "baseline-a",
                "baselineRepeat" => "baseline-b",
                _ => "feature",
            };
            string resultFile = lane.GetProperty("benchmarkDotNetResultFile").GetString()
                ?? throw new InvalidDataException($"{laneName} result file is missing");
            string resultPath = Path.Combine(directory, laneDirectory, resultFile);
            GpuPassFusionBaselineEvidence.VerifyFileHash(
                resultPath,
                lane.GetProperty("benchmarkDotNetResultSha256").GetString()
                    ?? throw new InvalidDataException($"{laneName} result hash is missing"),
                $"{laneName} benchmark result");

            string stdoutFile = lane.GetProperty("standardOutputFile").GetString()
                ?? throw new InvalidDataException($"{laneName} stdout file is missing");
            GpuPassFusionBaselineEvidence.VerifyFileHash(
                Path.Combine(directory, laneDirectory, stdoutFile),
                lane.GetProperty("standardOutputSha256").GetString()
                    ?? throw new InvalidDataException($"{laneName} stdout hash is missing"),
                $"{laneName} benchmark stdout");

            JsonElement counterHashes = lane.GetProperty("counterFileSha256");
            string counterDirectory = Path.Combine(directory, laneDirectory, "counters");
            Assert.That(
                counterHashes.EnumerateObject().Select(static property => property.Name),
                Is.EquivalentTo(ExpectedPairedBenchmarkCounterFiles));
            foreach (JsonProperty counter in counterHashes.EnumerateObject())
            {
                GpuPassFusionBaselineEvidence.VerifyFileHash(
                    Path.Combine(counterDirectory, counter.Name),
                    counter.Value.GetString() ?? throw new InvalidDataException($"{laneName} counter hash is null"),
                    $"{laneName} counter '{counter.Name}'");
            }
        }
    }

    [Test]
    public void FingerprintValidation_IsCompleteButEnvironmentIndependent()
    {
        Dictionary<string, IReadOnlyList<string>> foreignFingerprint =
            GpuPassFusionBaselineEvidence.RequiredFingerprintFields.ToDictionary(
                name => name,
                name => (IReadOnlyList<string>)(name == "vulkanEnabledExtensions"
                    ? ["VK_TEST_foreign_extension"]
                    : [$"foreign-evidence-{name}"]),
                StringComparer.Ordinal);

        Assert.That(
            () => GpuPassFusionBaselineEvidence.ValidateFingerprint(foreignFingerprint),
            Throws.Nothing,
            "Integrity validation must not select or reject blobs based on the current CI device.");
    }
    [Test]
    public void FingerprintValidation_RejectsMissingAndUnknownFields()
    {
        Dictionary<string, IReadOnlyList<string>> missing =
            GpuPassFusionBaselineEvidence.RequiredFingerprintFields
                .Skip(1)
                .ToDictionary(
                    name => name,
                    name => (IReadOnlyList<string>)[name],
                    StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> unknown =
            GpuPassFusionBaselineEvidence.RequiredFingerprintFields.ToDictionary(
                name => name,
                name => (IReadOnlyList<string>)(name == "deviceSelection" ? ["unknown"] : [name]),
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => GpuPassFusionBaselineEvidence.ValidateFingerprint(missing),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => GpuPassFusionBaselineEvidence.ValidateFingerprint(unknown),
                Throws.TypeOf<InvalidDataException>());
        }
    }

    [Test]
    public void HashIntegrity_MissingFileFailsWithoutGeneratingIt()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        string missingPath = Path.Combine(temporaryDirectory, "missing.rgba16f");
        try
        {
            Assert.That(
                () => GpuPassFusionBaselineEvidence.VerifyFileHash(
                    missingPath,
                    new string('0', 64),
                    "synthetic missing evidence"),
                Throws.TypeOf<FileNotFoundException>());
            Assert.That(File.Exists(missingPath), Is.False, "Integrity checks must never generate missing evidence.");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void HashIntegrity_MismatchFailsWithoutReplacingIt()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        string artifactPath = Path.Combine(temporaryDirectory, "mismatched.rgba16f");
        byte[] original = [0x42, 0x65, 0x75, 0x74, 0x6c];
        try
        {
            File.WriteAllBytes(artifactPath, original);

            Assert.That(
                () => GpuPassFusionBaselineEvidence.VerifyFileHash(
                    artifactPath,
                    new string('0', 64),
                    "synthetic mismatched evidence"),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                File.ReadAllBytes(artifactPath),
                Is.EqualTo(original),
                "Integrity checks must never replace mismatched evidence.");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void SameProcessParityHarness_UsesOnlyBothInternalModesAndFixedBounds()
    {
        var modes = new List<FusionMode>();

        GpuPassFusionParityResult result = GpuPassFusionSameProcessParityHarness.AssertParity(
            mode =>
            {
                modes.Add(mode);
                return CreateUniformBitmap(8, 8, red: 0.2f, green: 0.1f, blue: 0.3f, alpha: 0.5f);
            },
            new PixelRect(1, 1, 6, 6));

        MethodInfo method = typeof(GpuPassFusionSameProcessParityHarness).GetMethod(
            nameof(GpuPassFusionSameProcessParityHarness.AssertParity),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The same-process parity entry point is missing.");
        Type[] parameterTypes = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modes, Is.EqualTo(new[] { FusionMode.Disabled, FusionMode.Enabled }));
            Assert.That(
                parameterTypes,
                Is.EqualTo(new[] { typeof(Func<FusionMode, Bitmap>), typeof(PixelRect?) }),
                "The normal-CI harness must not accept a manifest, historical blob, or configurable bound.");
            Assert.That(GpuPassFusionSameProcessParityHarness.MinimumSsim, Is.EqualTo(0.99));
            Assert.That(GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim, Is.EqualTo(0.95));
            Assert.That(GpuPassFusionSameProcessParityHarness.MaximumLinearRgbMae, Is.EqualTo(0.02));
            Assert.That(GpuPassFusionSameProcessParityHarness.MaximumAlphaMae, Is.EqualTo(0.02));
            Assert.That(GpuPassFusionSameProcessParityHarness.MaximumAaEdgeChannelError, Is.EqualTo(0.02));
            Assert.That(result.FullImage.Ssim, Is.EqualTo(1));
            Assert.That(result.FullImage.WindowedSsim, Is.EqualTo(1));
            Assert.That(result.FullImage.LinearRgbMae, Is.Zero);
            Assert.That(result.FullImage.AlphaMae, Is.Zero);
            Assert.That(result.AaEdge, Is.Not.Null);
            Assert.That(result.AaEdge!.Value.MaximumError.Maximum, Is.Zero);
        }
    }

    [Test]
    public void SameProcessParityHarness_RejectsAaChannelErrorAboveFixedBound()
    {
        Assert.That(
            () => GpuPassFusionSameProcessParityHarness.AssertParity(
                mode => CreateUniformBitmap(
                    8,
                    8,
                    red: mode == FusionMode.Disabled ? 0.2f : 0.221f,
                    green: 0.1f,
                    blue: 0.3f,
                    alpha: 0.5f),
                new PixelRect(1, 1, 6, 6)),
            Throws.TypeOf<MultipleAssertException>()
                .With.Message.Contains("AA edge red-channel maximum error exceeded"),
            "The fixed AA per-channel maximum must reject an error above 0.02 even when mean RGB error passes.");
    }

    [Test]
    public void SameProcessParityHarness_RejectsLocalizedDefectThatPassesWholeFrameMetrics()
    {
        Assert.That(
            () => GpuPassFusionSameProcessParityHarness.AssertParity(
                mode => CreateCheckerboardBitmap(withLocalizedDefect: mode == FusionMode.Enabled)),
            Throws.TypeOf<MultipleAssertException>()
                .With.Message.Contains("minimum-window SSIM was too low"),
            "A localized defect must not hide inside acceptable whole-frame SSIM and MAE values.");
    }

    private static Bitmap CreateCheckerboardBitmap(bool withLocalizedDefect)
    {
        const int size = 128;
        var bitmap = new Bitmap(
            size,
            size,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        Span<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        ushort one = BitConverter.HalfToUInt16Bits((Half)1f);
        ushort zero = BitConverter.HalfToUInt16Bits((Half)0f);
        ushort gray = BitConverter.HalfToUInt16Bits((Half)0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = ((y * size) + x) * 4;
                ushort value = withLocalizedDefect && x < 14 && y < 14
                    ? gray
                    : ((x + y) & 1) == 0 ? one : zero;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
                pixels[offset + 3] = one;
            }
        }

        return bitmap;
    }

    private static Bitmap CreateUniformBitmap(
        int width,
        int height,
        float red,
        float green,
        float blue,
        float alpha)
    {
        var bitmap = new Bitmap(
            width,
            height,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        ushort[] pixel =
        [
            BitConverter.HalfToUInt16Bits((Half)red),
            BitConverter.HalfToUInt16Bits((Half)green),
            BitConverter.HalfToUInt16Bits((Half)blue),
            BitConverter.HalfToUInt16Bits((Half)alpha),
        ];
        for (int y = 0; y < height; y++)
        {
            Span<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < width; x++)
                pixel.CopyTo(row[(x * 4)..]);
        }

        return bitmap;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-gpu-pass-baseline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

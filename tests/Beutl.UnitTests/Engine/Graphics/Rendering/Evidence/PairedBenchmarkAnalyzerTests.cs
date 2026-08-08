using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Benchmarks.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

[TestFixture]
public sealed class PairedBenchmarkAnalyzerTests
{
    [Test]
    public void Run_ParsesSyntheticInputsAndWritesAcceptedManifest()
    {
        using var fixture = new AnalyzerFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.RunForTest(
            fixture.CreateArguments(),
            output,
            error,
            bootstrapIterations: 1000);

        Assert.That(exitCode, Is.Zero, error.ToString());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(fixture.OutputPath));
        JsonElement root = manifest.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("primaryAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("baselineRepeatStable").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("controlBarrierAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("overallAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(
                root.GetProperty("cases").EnumerateObject().Select(static item => item.Name),
                Is.EquivalentTo(RenderPipelineBenchmarkScenes.All.Select(static scene => scene.Name)));
            Assert.That(
                root.GetProperty("baselineHarnessFileSha256")
                    .EnumerateObject()
                    .Select(static item => item.Name),
                Does.Contain("../../../../../tests/Beutl.Benchmarks/Rendering/EvidenceFingerprintRules.cs"),
                "the manifest must authenticate source-linked inputs compiled into the target harness");
        });
    }

    [TestCase("--baseline-outputs")]
    [TestCase("--baseline-repeat-outputs")]
    [TestCase("--feature-outputs")]
    public void Run_RequiresEveryOutputDirectory(string option)
    {
        using var fixture = new AnalyzerFixture();
        var arguments = fixture.CreateArguments().ToList();
        int index = arguments.IndexOf(option);
        arguments.RemoveRange(index, 2);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(arguments.ToArray(), output, error);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain($"Missing required option '{option}'"));
    }

    [Test]
    public void Run_RejectsBootstrapIterationOverrides()
    {
        using var fixture = new AnalyzerFixture();
        string[] arguments = [.. fixture.CreateArguments(), "--bootstrap-iterations", "1000"];
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(arguments, output, error);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain("Unknown option(s): --bootstrap-iterations"));
    }

    [Test]
    public void FormalOptions_AlwaysUseTheRequiredBootstrapIterationCount()
    {
        using var fixture = new AnalyzerFixture();

        PairedBenchmarkAnalyzerOptions options = PairedBenchmarkAnalyzerOptions.Parse(fixture.CreateArguments());

        Assert.That(options.BootstrapIterations, Is.EqualTo(PairedBenchmarkAnalyzer.DefaultBootstrapIterations));
    }

    [TestCase(AcceptanceGate.Primary)]
    [TestCase(AcceptanceGate.BaselineRepeat)]
    [TestCase(AcceptanceGate.Control)]
    public void Analyze_ReportsIndependentAcceptanceGateFailures(AcceptanceGate gate)
    {
        using var fixture = new AnalyzerFixture();
        switch (gate)
        {
            case AcceptanceGate.Primary:
                fixture.SetSamples(AnalyzerRun.Feature, "ShaderOpacityShader", 125);
                break;
            case AcceptanceGate.BaselineRepeat:
                fixture.SetAllSamples(AnalyzerRun.BaselineRepeat, 130);
                break;
            case AcceptanceGate.Control:
                fixture.SetSamples(AnalyzerRun.Feature, "NoEffectControl", 125);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gate), gate, null);
        }

        PairedBenchmarkManifest manifest = fixture.Analyze();

        Assert.Multiple(() =>
        {
            Assert.That(manifest.OverallAcceptancePassed, Is.False);
            Assert.That(manifest.PrimaryAcceptancePassed, Is.EqualTo(gate != AcceptanceGate.Primary));
            Assert.That(manifest.BaselineRepeatStable, Is.EqualTo(gate != AcceptanceGate.BaselineRepeat));
            Assert.That(manifest.ControlBarrierAcceptancePassed, Is.EqualTo(gate != AcceptanceGate.Control));
        });
    }

    [Test]
    public void Analyze_RejectsIncompleteCaseSet()
    {
        using var fixture = new AnalyzerFixture();
        fixture.RemoveBenchmarkCase(AnalyzerRun.Feature, "StructuralToggle");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("StructuralToggle"));
    }

    [Test]
    public void Analyze_RejectsTruncatedRun()
    {
        using var fixture = new AnalyzerFixture();
        fixture.SetSampleCount(
            AnalyzerRun.Feature,
            "SingleShader",
            RenderPipelineBenchmarkConfig.BenchmarkIterationCount - 1,
            100);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("SingleShader")
                .And.Contain($"exactly {RenderPipelineBenchmarkConfig.BenchmarkIterationCount}")
                .And.Contain("baseline-a=15, feature=14, baseline-b=15"));
    }

    [TestCase("RenderPipeline(InvocationCount=1, IterationCount=15, LaunchCount=1, RunStrategy=Monitoring, UnrollFactor=1, WarmupCount=2)")]
    [TestCase("RenderPipeline(InvocationCount=1, IterationCount=14, LaunchCount=1, RunStrategy=Monitoring, UnrollFactor=1, WarmupCount=3)")]
    [TestCase("RenderPipeline(InvocationCount=1, IterationCount=15, LaunchCount=2, RunStrategy=Monitoring, UnrollFactor=1, WarmupCount=3)")]
    [TestCase("RenderPipeline(InvocationCount=2, IterationCount=15, LaunchCount=1, RunStrategy=Monitoring, UnrollFactor=1, WarmupCount=3)")]
    [TestCase("RenderPipeline(InvocationCount=1, IterationCount=15, LaunchCount=1, RunStrategy=Throughput, UnrollFactor=1, WarmupCount=3)")]
    [TestCase("RenderPipeline(InvocationCount=1, IterationCount=15, LaunchCount=1, RunStrategy=Monitoring, UnrollFactor=2, WarmupCount=3)")]
    public void Analyze_RejectsNonFrozenBenchmarkJob(string jobDisplay)
    {
        using var fixture = new AnalyzerFixture();
        fixture.SetJobDisplay(AnalyzerRun.Feature, jobDisplay);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("frozen RenderPipelineBenchmarkConfig job")
                .And.Contain(jobDisplay));
    }

    [TestCase("seed")]
    [TestCase("width")]
    [TestCase("height")]
    [TestCase("setupWarmupFrames")]
    [TestCase("lifetime")]
    [TestCase("requestShape")]
    [TestCase("semanticStageCount")]
    [TestCase("topLevelDrawableCount")]
    [TestCase("animation")]
    [TestCase("barrier")]
    [TestCase("hasStaticPrefixCache")]
    [TestCase("hasTargetDependencies")]
    public void Analyze_RejectsNonFrozenCounterWorkload(string field)
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "SingleShader",
            root =>
            {
                root[field] = field switch
                {
                    "seed" => RenderPipelineBenchmarkScenes.SourceSeed + 2,
                    "width" => RenderPipelineBenchmarkScenes.ReferenceSize.Width - 1,
                    "height" => RenderPipelineBenchmarkScenes.ReferenceSize.Height - 1,
                    "setupWarmupFrames" => RenderPipelineBenchmarkConfig.SetupWarmupFrameCount - 1,
                    "lifetime" => "short-lived-renderer",
                    "requestShape" => "reduced-target-request",
                    "semanticStageCount" => 99,
                    "topLevelDrawableCount" => 99,
                    "animation" => "StructuralToggle",
                    "barrier" => "TargetDependency",
                    "hasStaticPrefixCache" => true,
                    "hasTargetDependencies" => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
                };
            });

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("frozen benchmark").And.Contain(field).And.Contain("SingleShader"));
    }

    [Test]
    public void Analyze_RejectsSourceProvenanceMismatch()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "ShaderOpacityShader",
            root => root["fingerprint"]!["beutlEngineAssemblyVersion"] = "2.99.99+wrong-source");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("source provenance"));
    }

    [Test]
    public void Analyze_AcceptsVisualParityWhenFeatureOutputMatchesBaseline()
    {
        using var fixture = new AnalyzerFixture();

        Assert.That(() => fixture.Analyze(), Throws.Nothing);
    }

    [Test]
    public void Analyze_RejectsFeatureOutputThatDiffersVisuallyFromBaseline()
    {
        using var fixture = new AnalyzerFixture();
        string featureBlob = fixture.GetOutputBlobPath(
            AnalyzerRun.Feature,
            "ParameterOnlyAnimation",
            OutputPhase.Setup);
        byte[] corrupted = File.ReadAllBytes(featureBlob);
        for (int index = 0; index < corrupted.Length; index += 2)
            corrupted[index] ^= 0xff;
        fixture.ReplaceOutputBlob(
            AnalyzerRun.Feature,
            "ParameterOnlyAnimation",
            OutputPhase.Setup,
            corrupted,
            updateCounterContract: true);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("visually equivalent").And.Contain("ParameterOnlyAnimation"));
    }

    [TestCase(OutputPhase.Setup)]
    [TestCase(OutputPhase.Measured)]
    public void Analyze_RejectsOutputBlobWhoseHashDoesNotMatchItsCounter(OutputPhase phase)
    {
        using var fixture = new AnalyzerFixture();
        string path = fixture.GetOutputBlobPath(AnalyzerRun.Feature, "ShaderOpacityShader", phase);
        byte[] corrupted = File.ReadAllBytes(path);
        corrupted[0] ^= 0xff;
        fixture.ReplaceOutputBlob(
            AnalyzerRun.Feature,
            "ShaderOpacityShader",
            phase,
            corrupted,
            updateCounterContract: false);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
                Does.Contain("SHA-256").And.Contain("ShaderOpacityShader").And.Contain(phase.ToString()).IgnoreCase);
    }

    [TestCase(OutputPhase.Setup)]
    [TestCase(OutputPhase.Measured)]
    public void Analyze_RejectsOutputBlobWhoseChecksumDoesNotMatchItsCounter(OutputPhase phase)
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "ParameterOnlyAnimation",
            root =>
            {
                if (phase == OutputPhase.Setup)
                {
                    root["outputChecksum"] = "cccccccccccccccc";
                }
                else
                {
                    root["measuredOutputChecksum"] = "cccccccccccccccc";
                    root["expectedMeasuredOutputChecksum"] = "cccccccccccccccc";
                }
            });

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("checksum")
                .And.Contain("ParameterOnlyAnimation")
                .And.Contain(phase.ToString()).IgnoreCase);
    }

    [TestCase(OutputPhase.Setup)]
    [TestCase(OutputPhase.Measured)]
    public void Analyze_RejectsBaselineRepeatBlobWhoseHashDoesNotMatchItsCounter(OutputPhase phase)
    {
        using var fixture = new AnalyzerFixture();
        string path = fixture.GetOutputBlobPath(
            AnalyzerRun.BaselineRepeat,
            "ShaderOpacityShader",
            phase);
        byte[] corrupted = File.ReadAllBytes(path);
        corrupted[0] ^= 0xff;
        fixture.ReplaceOutputBlob(
            AnalyzerRun.BaselineRepeat,
            "ShaderOpacityShader",
            phase,
            corrupted,
            updateCounterContract: false);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("SHA-256")
                .And.Contain("baseline repeat")
                .And.Contain("ShaderOpacityShader")
                .And.Contain(phase.ToString()).IgnoreCase);
    }

    [Test]
    public void Analyze_RejectsMissingOutputBlobContract()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "ShaderOpacityShader",
            root => root["measuredOutputBlobFile"] = string.Empty);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("measuredOutputBlobFile"));
    }

    [Test]
    public void Analyze_RejectsMissingOutputBlobFile()
    {
        using var fixture = new AnalyzerFixture();
        File.Delete(fixture.GetOutputBlobPath(
            AnalyzerRun.Feature,
            "ShaderOpacityShader",
            OutputPhase.Measured));

        FileNotFoundException? exception = Assert.Throws<FileNotFoundException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("feature measured").And.Contain("ShaderOpacityShader"));
    }

    [Test]
    public void Analyze_RejectsAnimatedMeasuredOutputThatDiffersFromBaseline()
    {
        using var fixture = new AnalyzerFixture();
        string path = fixture.GetOutputBlobPath(
            AnalyzerRun.Feature,
            "ParameterOnlyAnimation",
            OutputPhase.Measured);
        byte[] corrupted = File.ReadAllBytes(path);
        for (int index = 0; index < corrupted.Length; index += 2)
            corrupted[index] ^= 0xff;
        fixture.ReplaceOutputBlob(
            AnalyzerRun.Feature,
            "ParameterOnlyAnimation",
            OutputPhase.Measured,
            corrupted,
            updateCounterContract: true);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("feature measured output")
                .And.Contain("visually equivalent")
                .And.Contain("ParameterOnlyAnimation"));
    }

    [Test]
    public void Analyze_RejectsCrossPipelineOutputBoundsShift()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "ShaderOpacityShader",
            root =>
            {
                root["outputBounds"]!["x"] = 1;
                root["measuredOutputBounds"]!["x"] = 1;
                root["expectedMeasuredOutputBounds"]!["x"] = 1;
            });

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("Cross-pipeline setup output geometry")
                .And.Contain("outputBounds")
                .And.Contain("ShaderOpacityShader"));
    }

    [Test]
    public void BenchmarkHarnessProvenance_ExtractsTheCompiledSourceRevision()
    {
        const string revision = "1111111111111111111111111111111111111111";

        Assert.That(
            BenchmarkHarnessProvenance.ExtractSourceRevision($"2.99.99+{revision}"),
            Is.EqualTo(revision));
        Assert.That(
            () => BenchmarkHarnessProvenance.ExtractSourceRevision("2.99.99+short"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void PairedRunner_RejectsHarnessesThatDoNotAuthenticateTheExecutedBinary()
    {
        string runner = File.ReadAllText(Path.Combine(
            GpuPassFusionEvidencePaths.Discover().EvidenceDirectory,
            "run-paired-benchmarks.sh"));

        Assert.Multiple(() =>
        {
            Assert.That(runner, Does.Contain("BEUTL_RENDER_BENCHMARK_HARNESS_PROVENANCE"));
            Assert.That(runner, Does.Contain("harness-provenance.json"));
            Assert.That(runner, Does.Contain("Benchmark harness source revision mismatch"));
            Assert.That(runner, Does.Contain("Beutl.GpuPassTargetBenchmarkHarness"));
            Assert.That(runner, Does.Contain("Beutl.Benchmarks"));
        });
    }

    [Test]
    public void TargetHarness_CompletesVerifiedObservationBeforeRetainingItsBitmap()
    {
        string source = File.ReadAllText(Path.Combine(
            GpuPassFusionEvidencePaths.Discover().EvidenceDirectory,
            "target-benchmark-harness",
            "TargetRenderPipelineBenchmarks.cs"));
        int methodStart = source.IndexOf(
            "private TargetObservedFrame RenderAndObserve(",
            StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        int methodEnd = source.IndexOf(
            "private static TargetObservedFrame CompleteObservation(",
            methodStart,
            StringComparison.Ordinal);
        Assert.That(methodEnd, Is.GreaterThan(methodStart));
        string method = source[methodStart..methodEnd];

        int completeObservation = method.IndexOf(
            "observed = CompleteObservation(frame, bitmap, counters);",
            StringComparison.Ordinal);
        int retainSetup = method.IndexOf("_lastSetupBitmap = bitmap;", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(completeObservation, Is.GreaterThanOrEqualTo(0));
            Assert.That(retainSetup, Is.GreaterThan(completeObservation),
                "the verified setup bitmap must be hashed before local ownership is transferred");
        });
    }

    [Test]
    public void Analyze_RejectsFingerprintSchemaMismatch()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Baseline,
            "SingleShader",
            root => root["fingerprint"]!.AsObject().Remove("rendererBackend"));

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("fingerprint schema mismatch").IgnoreCase);
    }

    [Test]
    public void Analyze_RejectsUnknownFingerprintArrayElement()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "SingleShader",
            root => root["fingerprint"]!["vulkanEnabledExtensions"] = new JsonArray(
                "VK_KHR_surface",
                "unknown"));

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("vulkanEnabledExtensions").And.Contain("unknown"));
    }

    [TestCase(OutputMismatch.PairedExact)]
    [TestCase(OutputMismatch.SelfRecorded)]
    [TestCase(OutputMismatch.FeatureStatic)]
    [TestCase(OutputMismatch.FeatureGeometry)]
    public void Analyze_RejectsOutputContractMismatches(OutputMismatch mismatch)
    {
        using var fixture = new AnalyzerFixture();
        switch (mismatch)
        {
            case OutputMismatch.PairedExact:
                fixture.MutateCounter(
                    AnalyzerRun.Feature,
                    "NoEffectControl",
                    root => root["outputSha256"] = AnalyzerFixture.AlternateSha256);
                break;
            case OutputMismatch.SelfRecorded:
                fixture.MutateCounter(
                    AnalyzerRun.Baseline,
                    "SingleShader",
                    root => root["expectedMeasuredOutputChecksum"] = AnalyzerFixture.AlternateChecksum);
                break;
            case OutputMismatch.FeatureStatic:
                fixture.MutateCounter(
                    AnalyzerRun.Feature,
                    "SingleShader",
                    root =>
                    {
                        root["measuredOutputSha256"] = AnalyzerFixture.AlternateSha256;
                        root["expectedMeasuredOutputSha256"] = AnalyzerFixture.AlternateSha256;
                    });
                break;
            case OutputMismatch.FeatureGeometry:
                fixture.MutateCounter(
                    AnalyzerRun.Feature,
                    "ParameterOnlyAnimation",
                    root =>
                    {
                        root["measuredWidth"] = 385;
                        root["expectedMeasuredWidth"] = 385;
                    });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null);
        }

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("output").IgnoreCase);
    }

    [TestCase("\"NaN, NaN, NaN, NaN\"")]
    [TestCase("\"0, 0, 0, 0\"")]
    [TestCase("{\"left\":0,\"top\":0,\"right\":384,\"bottom\":216}")]
    [TestCase("{\"x\":0,\"y\":0,\"width\":385,\"height\":216}")]
    public void Analyze_RejectsMalformedOrDimensionallyInconsistentOutputBounds(string json)
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Feature,
            "SingleShader",
            root => root["outputBounds"] = JsonNode.Parse(json));

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("output").IgnoreCase);
    }

    [Test]
    public void Analyze_AcceptsBlankDriverInfoOnlyForACpuDevice()
    {
        using var accepted = new AnalyzerFixture();
        accepted.MutateCounterFingerprints(root =>
        {
            root["vulkanDeviceType"] = "Cpu";
            root["vulkanDriverInfo"] = string.Empty;
        });
        using var rejected = new AnalyzerFixture();
        rejected.MutateCounterFingerprints(root =>
        {
            root["vulkanDeviceType"] = "DiscreteGpu";
            root["vulkanDriverInfo"] = string.Empty;
        });

        Assert.That(() => accepted.Analyze(), Throws.Nothing);
        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => rejected.Analyze());
        Assert.That(exception!.Message, Does.Contain("vulkanDriverInfo"));
    }

    [Test]
    public void Analyze_RejectsAnUnknownDriverInfoEvenOnACpuDevice()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounterFingerprints(root =>
        {
            root["vulkanDeviceType"] = "Cpu";
            root["vulkanDriverInfo"] = "unknown";
        });

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("vulkanDriverInfo"));
    }

    [Test]
    public void Analyze_RejectsABlankFieldOtherThanDriverInfoOnACpuDevice()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounterFingerprints(root =>
        {
            root["vulkanDeviceType"] = "Cpu";
            root["vulkanDeviceName"] = string.Empty;
        });

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("vulkanDeviceName"));
    }

    [Test]
    public void FingerprintCapture_AcceptsBlankDriverInfoOnlyForACpuDevice()
    {
        RenderPipelineEvidenceFingerprint cpu = CreateFingerprint("Cpu", driverInfo: string.Empty);
        RenderPipelineEvidenceFingerprint gpu = CreateFingerprint("DiscreteGpu", driverInfo: string.Empty);
        RenderPipelineEvidenceFingerprint unknownOnCpu = CreateFingerprint("Cpu", driverInfo: "unknown");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => RenderPipelineEvidenceFingerprint.Validate(cpu), Throws.Nothing);
            Assert.That(
                () => RenderPipelineEvidenceFingerprint.Validate(gpu),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => RenderPipelineEvidenceFingerprint.Validate(unknownOnCpu),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void FrozenWorkloadExtents_MatchTheGeometryTheBenchmarkDraws()
    {
        var sizes = RenderPipelineBenchmarkScenes.All.ToDictionary(
            static scene => scene.Name,
            RenderPipelineBenchmarkScenes.GetOutputSize,
            StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sizes["SingleShader"], Is.EqualTo(RenderPipelineBenchmarkScenes.ReferenceSize));
            Assert.That(sizes["SmallObjectFixedOverhead"], Is.EqualTo(new PixelSize(38, 22)));
            Assert.That(
                sizes["MultipleDrawablesTargetDependencies"],
                Is.EqualTo(new PixelSize(360, 192)));
            Assert.That(
                sizes.Values,
                Has.Some.Not.EqualTo(RenderPipelineBenchmarkScenes.ReferenceSize),
                "A per-scene extent check is only meaningful while some scene differs from the reference size.");
        }
    }

    private static RenderPipelineEvidenceFingerprint CreateFingerprint(string deviceType, string driverInfo)
    {
        var fingerprint = new RenderPipelineEvidenceFingerprint
        {
            VulkanEnabledExtensions = ["VK_KHR_surface"],
        };
        foreach (System.Reflection.PropertyInfo property in
                 typeof(RenderPipelineEvidenceFingerprint).GetProperties())
        {
            if (property.PropertyType == typeof(string))
                property.SetValue(fingerprint, property.Name);
        }

        typeof(RenderPipelineEvidenceFingerprint)
            .GetProperty(nameof(RenderPipelineEvidenceFingerprint.VulkanDeviceType))!
            .SetValue(fingerprint, deviceType);
        typeof(RenderPipelineEvidenceFingerprint)
            .GetProperty(nameof(RenderPipelineEvidenceFingerprint.VulkanDriverInfo))!
            .SetValue(fingerprint, driverInfo);
        return fingerprint;
    }

    public enum AcceptanceGate
    {
        Primary,
        BaselineRepeat,
        Control,
    }

    public enum OutputMismatch
    {
        PairedExact,
        SelfRecorded,
        FeatureStatic,
        FeatureGeometry,
    }

    private enum AnalyzerRun
    {
        Baseline,
        BaselineRepeat,
        Feature,
    }

    public enum OutputPhase
    {
        Setup,
        Measured,
    }

    private sealed class AnalyzerFixture : IDisposable
    {
        public const string AlternateSha256 =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        public const string AlternateChecksum = "dddddddddddddddd";

        private const string BaselineSha = "43a38e665d9bf52548161a3917e748bd1457ff55";
        private const string FeatureSha = "1111111111111111111111111111111111111111";
        private const string RunnerSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222";
        private static readonly string[] s_fingerprintFields =
        [
            "beutlEngineAssemblyVersion",
            "deviceSelection",
            "environmentVersion",
            "frameworkDescription",
            "metalDeviceName",
            "metalDriver",
            "metalFeatureFamily",
            "metalRegistryId",
            "osArchitecture",
            "osBuild",
            "osDescription",
            "osVersion",
            "processArchitecture",
            "rendererBackend",
            "runtimeIdentifier",
            "silkNetVulkanVersion",
            "skiaBackend",
            "skiaSharpManagedVersion",
            "skiaSharpNativeVersion",
            "vulkanApiVersion",
            "vulkanDeviceId",
            "vulkanDeviceName",
            "vulkanDeviceType",
            "vulkanDeviceUuid",
            "vulkanDriverId",
            "vulkanDriverInfo",
            "vulkanDriverName",
            "vulkanDriverUuid",
            "vulkanDriverVersionDecoded",
            "vulkanDriverVersionRaw",
            "vulkanEnabledExtensions",
            "vulkanVendorId",
        ];

        private readonly Dictionary<AnalyzerRun, RunPaths> _runs;

        public AnalyzerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"beutl-paired-analyzer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            _runs = new Dictionary<AnalyzerRun, RunPaths>
            {
                [AnalyzerRun.Baseline] = CreateRun("baseline-a", BaselineSha, 100),
                [AnalyzerRun.BaselineRepeat] = CreateRun("baseline-b", BaselineSha, 100),
                [AnalyzerRun.Feature] = CreateRun("feature", FeatureSha, 100),
            };
            SetSamples(AnalyzerRun.Feature, "ShaderOpacityShader", 80);
            OutputPath = Path.Combine(Root, "paired-manifest.json");
        }

        public string Root { get; }

        public string OutputPath { get; }

        public PairedBenchmarkManifest Analyze()
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions());

        public string[] CreateArguments()
        {
            RunPaths baseline = _runs[AnalyzerRun.Baseline];
            RunPaths repeat = _runs[AnalyzerRun.BaselineRepeat];
            RunPaths feature = _runs[AnalyzerRun.Feature];
            return
            [
                "--baseline-results", baseline.Results,
                "--baseline-repeat-results", repeat.Results,
                "--feature-results", feature.Results,
                "--baseline-counters", baseline.Counters,
                "--baseline-repeat-counters", repeat.Counters,
                "--feature-counters", feature.Counters,
                "--baseline-stdout", baseline.Stdout,
                "--baseline-repeat-stdout", repeat.Stdout,
                "--feature-stdout", feature.Stdout,
                "--baseline-outputs", baseline.OutputBlobs,
                "--baseline-repeat-outputs", repeat.OutputBlobs,
                "--feature-outputs", feature.OutputBlobs,
                "--baseline-sha", BaselineSha,
                "--feature-sha", FeatureSha,
                "--baseline-command", "synthetic baseline A",
                "--baseline-repeat-command", "synthetic baseline B",
                "--feature-command", "synthetic feature",
                "--runner-sha256", RunnerSha256,
                "--baseline-harness", GpuPassFusionEvidencePaths.Discover().EvidenceDirectory
                    + Path.DirectorySeparatorChar + "target-benchmark-harness",
                "--output", OutputPath,
            ];
        }

        public void SetSamples(AnalyzerRun run, string caseName, double value)
            => SetSampleCount(
                run,
                caseName,
                RenderPipelineBenchmarkConfig.BenchmarkIterationCount,
                value);

        public void SetSampleCount(AnalyzerRun run, string caseName, int count, double value)
        {
            JsonObject root = LoadObject(_runs[run].Results);
            JsonObject benchmark = root["Benchmarks"]!.AsArray()
                .Select(static item => item!.AsObject())
                .Single(item => string.Equals(
                    item["Parameters"]!.GetValue<string>(),
                    $"CaseName={caseName}",
                    StringComparison.Ordinal));
            benchmark["Statistics"]!["OriginalValues"] = Samples(value, count);
            WriteObject(_runs[run].Results, root);
        }

        public void SetAllSamples(AnalyzerRun run, double value)
        {
            JsonObject root = LoadObject(_runs[run].Results);
            foreach (JsonNode? item in root["Benchmarks"]!.AsArray())
                item!["Statistics"]!["OriginalValues"] = Samples(value);
            WriteObject(_runs[run].Results, root);
        }

        public void SetJobDisplay(AnalyzerRun run, string jobDisplay)
        {
            JsonObject root = LoadObject(_runs[run].Results);
            foreach (JsonNode? item in root["Benchmarks"]!.AsArray())
            {
                string caseName = item!["Parameters"]!.GetValue<string>()["CaseName=".Length..];
                item["DisplayInfo"] = $"Synthetic.Render: {jobDisplay} [CaseName={caseName}]";
            }
            WriteObject(_runs[run].Results, root);
        }

        public void RemoveBenchmarkCase(AnalyzerRun run, string caseName)
        {
            JsonObject root = LoadObject(_runs[run].Results);
            JsonArray benchmarks = root["Benchmarks"]!.AsArray();
            JsonNode item = benchmarks.Single(value => string.Equals(
                value!["Parameters"]!.GetValue<string>(),
                $"CaseName={caseName}",
                StringComparison.Ordinal))!;
            benchmarks.Remove(item);
            WriteObject(_runs[run].Results, root);
        }

        public void MutateCounterFingerprints(Action<JsonObject> mutate)
        {
            foreach (AnalyzerRun run in Enum.GetValues<AnalyzerRun>())
            {
                foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
                    MutateCounter(run, scene.Name, root => mutate(root["fingerprint"]!.AsObject()));
            }
        }

        public void MutateCounter(AnalyzerRun run, string caseName, Action<JsonObject> mutate)
        {
            string path = Path.Combine(_runs[run].Counters, caseName + ".json");
            JsonObject root = LoadObject(path);
            mutate(root);
            WriteObject(path, root);
        }

        public string GetOutputBlobPath(AnalyzerRun run, string caseName, OutputPhase phase)
            => Path.Combine(_runs[run].OutputBlobs, OutputBlobFile(caseName, phase));

        public void ReplaceOutputBlob(
            AnalyzerRun run,
            string caseName,
            OutputPhase phase,
            byte[] payload,
            bool updateCounterContract)
        {
            File.WriteAllBytes(GetOutputBlobPath(run, caseName, phase), payload);
            if (!updateCounterContract)
                return;

            string sha256 = Sha256(payload);
            string checksum = Checksum(payload);
            MutateCounter(run, caseName, root =>
            {
                if (phase == OutputPhase.Setup)
                {
                    root["outputSha256"] = sha256;
                    root["outputChecksum"] = checksum;
                }
                else
                {
                    root["measuredOutputSha256"] = sha256;
                    root["measuredOutputChecksum"] = checksum;
                    root["expectedMeasuredOutputSha256"] = sha256;
                    root["expectedMeasuredOutputChecksum"] = checksum;
                }
            });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private PairedBenchmarkAnalyzerOptions CreateOptions()
        {
            RunPaths baseline = _runs[AnalyzerRun.Baseline];
            RunPaths repeat = _runs[AnalyzerRun.BaselineRepeat];
            RunPaths feature = _runs[AnalyzerRun.Feature];
            return new PairedBenchmarkAnalyzerOptions
            {
                BaselineResultsPath = baseline.Results,
                BaselineRepeatResultsPath = repeat.Results,
                FeatureResultsPath = feature.Results,
                BaselineCountersPath = baseline.Counters,
                BaselineRepeatCountersPath = repeat.Counters,
                FeatureCountersPath = feature.Counters,
                BaselineStdoutPath = baseline.Stdout,
                BaselineRepeatStdoutPath = repeat.Stdout,
                FeatureStdoutPath = feature.Stdout,
                BaselineOutputsPath = baseline.OutputBlobs,
                BaselineRepeatOutputsPath = repeat.OutputBlobs,
                FeatureOutputsPath = feature.OutputBlobs,
                BaselineSha = BaselineSha,
                FeatureSha = FeatureSha,
                BaselineCommand = "synthetic baseline A",
                BaselineRepeatCommand = "synthetic baseline B",
                FeatureCommand = "synthetic feature",
                RunnerSha256 = RunnerSha256,
                BaselineHarnessPath = Path.Combine(
                    GpuPassFusionEvidencePaths.Discover().EvidenceDirectory,
                    "target-benchmark-harness"),
                OutputPath = OutputPath,
                BootstrapIterations = 1000,
            };
        }

        private RunPaths CreateRun(string name, string sourceSha, double sampleValue)
        {
            string directory = Path.Combine(Root, name);
            string counters = Path.Combine(directory, "counters");
            Directory.CreateDirectory(counters);
            string results = Path.Combine(directory, "results.json");
            string stdout = Path.Combine(directory, "stdout.txt");
            string outputBlobs = Path.Combine(directory, "output-blobs");
            Directory.CreateDirectory(outputBlobs);
            WriteBenchmarkResults(results, sampleValue);
            File.WriteAllText(stdout, "synthetic benchmark output\n", new UTF8Encoding(false));
            foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
            {
                byte[] setupBlob = CreateSyntheticBlob(scene);
                byte[] measuredBlob = CreateSyntheticBlob(scene);
                File.WriteAllBytes(
                    Path.Combine(outputBlobs, OutputBlobFile(scene.Name, OutputPhase.Setup)),
                    setupBlob);
                File.WriteAllBytes(
                    Path.Combine(outputBlobs, OutputBlobFile(scene.Name, OutputPhase.Measured)),
                    measuredBlob);
                WriteCounter(
                    Path.Combine(counters, scene.Name + ".json"),
                    scene,
                    sourceSha,
                    Sha256(setupBlob),
                    Checksum(setupBlob),
                    Sha256(measuredBlob),
                    Checksum(measuredBlob));
            }

            return new RunPaths(results, counters, stdout, outputBlobs);
        }

        private static byte[] CreateSyntheticBlob(RenderPipelineBenchmarkSceneDefinition scene)
        {
            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            int width = size.Width;
            int height = size.Height;
            var payload = new byte[checked(width * height * 8)];
            for (int offset = 0; offset < payload.Length; offset += 8)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    payload.AsSpan(offset, 2),
                    BitConverter.HalfToUInt16Bits((Half)0.5f));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    payload.AsSpan(offset + 2, 2),
                    BitConverter.HalfToUInt16Bits((Half)0.5f));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    payload.AsSpan(offset + 4, 2),
                    BitConverter.HalfToUInt16Bits((Half)0.5f));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    payload.AsSpan(offset + 6, 2),
                    BitConverter.HalfToUInt16Bits((Half)1f));
            }

            return payload;
        }

        private static void WriteBenchmarkResults(string path, double sampleValue)
        {
            var benchmarks = new JsonArray();
            foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
            {
                benchmarks.Add(new JsonObject
                {
                    ["FullName"] = $"Synthetic.Render(CaseName: \"{scene.Name}\")",
                    ["Parameters"] = $"CaseName={scene.Name}",
                    ["Method"] = "Render",
                    ["DisplayInfo"] =
                        $"Synthetic.Render: {RenderPipelineBenchmarkConfig.ExpectedJobDisplay} "
                        + $"[CaseName={scene.Name}]",
                    ["Statistics"] = new JsonObject { ["OriginalValues"] = Samples(sampleValue) },
                });
            }

            WriteObject(path, new JsonObject
            {
                ["HostEnvironmentInfo"] = new JsonObject { ["BenchmarkDotNetVersion"] = "0.15.8" },
                ["Benchmarks"] = benchmarks,
            });
        }

        private static void WriteCounter(
            string path,
            RenderPipelineBenchmarkSceneDefinition scene,
            string sourceSha,
            string setupOutputSha256,
            string setupOutputChecksum,
            string measuredOutputSha256,
            string measuredOutputChecksum)
        {
            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            WriteObject(path, new JsonObject
            {
                ["schemaVersion"] = 3,
                ["caseName"] = scene.Name,
                ["seed"] = scene.Seed,
                ["width"] = size.Width,
                ["height"] = size.Height,
                ["setupWarmupFrames"] = RenderPipelineBenchmarkConfig.SetupWarmupFrameCount,
                ["lifetime"] = RenderPipelineBenchmarkConfig.LifetimeContract,
                ["requestShape"] = RenderPipelineBenchmarkConfig.RequestShapeContract,
                ["semanticStageCount"] = scene.SemanticStageCount,
                ["topLevelDrawableCount"] = scene.TopLevelDrawableCount,
                ["animation"] = scene.Animation.ToString(),
                ["barrier"] = scene.Barrier.ToString(),
                ["hasStaticPrefixCache"] = scene.HasStaticPrefixCache,
                ["hasTargetDependencies"] = scene.HasTargetDependencies,
                ["setupOutputBlobFile"] = OutputBlobFile(scene.Name, OutputPhase.Setup),
                ["measuredOutputBlobFile"] = OutputBlobFile(scene.Name, OutputPhase.Measured),
                ["outputSha256"] = setupOutputSha256,
                ["outputChecksum"] = setupOutputChecksum,
                ["outputBounds"] = Bounds(size),
                ["measuredOutputSha256"] = measuredOutputSha256,
                ["measuredOutputChecksum"] = measuredOutputChecksum,
                ["measuredOutputBounds"] = Bounds(size),
                ["measuredWidth"] = size.Width,
                ["measuredHeight"] = size.Height,
                ["expectedMeasuredOutputSha256"] = measuredOutputSha256,
                ["expectedMeasuredOutputChecksum"] = measuredOutputChecksum,
                ["expectedMeasuredOutputBounds"] = Bounds(size),
                ["expectedMeasuredWidth"] = size.Width,
                ["expectedMeasuredHeight"] = size.Height,
                ["setupLastRequestCounters"] = new JsonObject { ["Requests"] = 1 },
                ["measuredLastRequestCounters"] = new JsonObject { ["Requests"] = 1 },
                ["fingerprint"] = Fingerprint(sourceSha),
            });
        }

        private static JsonObject Fingerprint(string sourceSha)
        {
            var result = new JsonObject();
            foreach (string name in s_fingerprintFields)
            {
                result[name] = name switch
                {
                    "beutlEngineAssemblyVersion" => $"2.99.99+{sourceSha}",
                    "vulkanEnabledExtensions" => new JsonArray("VK_KHR_surface"),
                    _ => "synthetic-environment",
                };
            }
            return result;
        }

        private static JsonArray Samples(double value)
            => Samples(value, RenderPipelineBenchmarkConfig.BenchmarkIterationCount);

        private static JsonArray Samples(double value, int count)
            => new(Enumerable.Repeat(value, count).Select(v => JsonValue.Create(v)).ToArray());

        private static JsonObject Bounds(PixelSize size)
            => new()
            {
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = size.Width,
                ["height"] = size.Height,
            };

        private static string OutputBlobFile(string caseName, OutputPhase phase)
            => $"{caseName}.{(phase == OutputPhase.Setup ? "setup" : "measured")}.rgba16f";

        private static string Sha256(byte[] payload)
            => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        private static string Checksum(ReadOnlySpan<byte> payload)
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;
            ulong result = offset;
            for (int byteOffset = 0; byteOffset < payload.Length; byteOffset += 26)
            {
                result ^= System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(byteOffset, 2));
                result *= prime;
            }
            return result.ToString("x16");
        }

        private static JsonObject LoadObject(string path)
            => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();

        private static void WriteObject(string path, JsonObject value)
            => File.WriteAllText(
                path,
                value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));

        internal sealed record RunPaths(string Results, string Counters, string Stdout, string OutputBlobs);
    }
}

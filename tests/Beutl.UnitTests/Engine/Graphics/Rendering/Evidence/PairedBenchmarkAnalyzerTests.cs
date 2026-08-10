using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Benchmarks.Rendering;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

[TestFixture]
public sealed class PairedBenchmarkAnalyzerTests
{
    [Test]
    public void Run_WritesAcceptedManifestFromSyntheticInputs()
    {
        using var fixture = new AnalyzerFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = fixture.Run(output, error);

        Assert.That(exitCode, Is.Zero, error.ToString());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(fixture.OutputPath));
        JsonElement root = manifest.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("primaryAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("baselineRepeatStable").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("controlBarrierAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("overallAcceptancePassed").GetBoolean(), Is.True);
            Assert.That(
                root.GetProperty("cases").EnumerateObject().Select(static item => item.Name),
                Is.EquivalentTo(RenderPipelineBenchmarkScenes.All.Select(static scene => scene.Name)));
            Assert.That(
                root.GetProperty("baselineHarnessBuildInputSha256")
                    .EnumerateObject()
                    .Select(static item => item.Name),
                Does.Contain("tests/Beutl.Benchmarks/Rendering/EvidenceFingerprintRules.cs"),
                "the manifest must authenticate source-linked inputs compiled into the target harness");
            Assert.That(
                root.GetProperty("featureHarnessBuildInputSha256")
                    .EnumerateObject()
                    .Select(static item => item.Name),
                Does.Contain("tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs"),
                "the accepted manifest must bind the feature workload implementation bytes");
            Assert.That(root.GetProperty("baseline").GetProperty("codeSha").GetString(), Is.EqualTo(AnalyzerFixture.BaselineSha));
            Assert.That(root.GetProperty("baseline").GetProperty("harnessSourceRevision").GetString(), Is.EqualTo(AnalyzerFixture.FeatureSha));
            Assert.That(root.GetProperty("baselineRepeat").GetProperty("codeSha").GetString(), Is.EqualTo(AnalyzerFixture.BaselineSha));
            Assert.That(root.GetProperty("baselineRepeat").GetProperty("harnessSourceRevision").GetString(), Is.EqualTo(AnalyzerFixture.FeatureSha));
            Assert.That(root.GetProperty("feature").GetProperty("codeSha").GetString(), Is.EqualTo(AnalyzerFixture.FeatureSha));
            Assert.That(root.GetProperty("feature").GetProperty("harnessSourceRevision").GetString(), Is.EqualTo(AnalyzerFixture.FeatureSha));
            Assert.That(
                root.GetProperty("feature").GetProperty("executedHarnessAssemblyFile").GetString(),
                Is.EqualTo(Path.GetFileName(fixture.FeatureRun.HarnessAssembly)));
            Assert.That(
                root.GetProperty("feature").GetProperty("executedHarnessAssemblySha256").GetString(),
                Is.EqualTo(Sha256File(fixture.FeatureRun.HarnessAssembly)));
            Assert.That(
                root.GetProperty("feature").GetProperty("benchmarkDotNetArtifactSha256")
                    .GetProperty(Path.GetFileName(fixture.FeatureRun.HarnessAssembly)).GetString(),
                Is.EqualTo(Sha256File(fixture.FeatureRun.HarnessAssembly)));
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
    public void Analyze_BindsEveryOutputBlobIntoRunManifestsAndArchiveHashes()
    {
        using var fixture = new AnalyzerFixture();

        PairedBenchmarkManifest manifest = fixture.Analyze();
        var lanes = new[]
        {
            (Run: AnalyzerRun.Baseline, Manifest: manifest.Baseline),
            (Run: AnalyzerRun.BaselineRepeat, Manifest: manifest.BaselineRepeat),
            (Run: AnalyzerRun.Feature, Manifest: manifest.Feature),
        };
        string[] expectedFiles = RenderPipelineBenchmarkScenes.All
            .SelectMany(static scene => new[]
            {
                $"{scene.Name}.setup.rgba16f",
                $"{scene.Name}.measured.rgba16f",
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            foreach ((AnalyzerRun run, PairedBenchmarkRunManifest runManifest) in lanes)
            {
                Assert.That(runManifest.OutputDirectory, Is.EqualTo("output-blobs"));
                Assert.That(runManifest.OutputBlobFileSha256.Keys, Is.EqualTo(expectedFiles));
                foreach (string file in expectedFiles)
                {
                    string expectedSha256 = Convert.ToHexString(SHA256.HashData(
                        File.ReadAllBytes(fixture.GetOutputBlobPath(run, file)))).ToLowerInvariant();
                    Assert.That(runManifest.OutputBlobFileSha256[file], Is.EqualTo(expectedSha256));
                    Assert.That(
                        runManifest.BenchmarkDotNetArtifactSha256[$"output-blobs/{file}"],
                        Is.EqualTo(expectedSha256));
                }
            }
        });
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

    [TestCase("--baseline-harness-assembly")]
    [TestCase("--baseline-repeat-harness-assembly")]
    [TestCase("--feature-harness-assembly")]
    public void Run_RequiresEveryExecutedHarnessAssemblySnapshot(string option)
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

    [TestCase("--baseline-results")]
    [TestCase("--baseline-repeat-results")]
    [TestCase("--feature-results")]
    [TestCase("--baseline-stdout")]
    [TestCase("--baseline-repeat-stdout")]
    [TestCase("--feature-stdout")]
    [TestCase("--baseline-harness-provenance")]
    [TestCase("--baseline-repeat-harness-provenance")]
    [TestCase("--feature-harness-provenance")]
    [TestCase("--baseline-harness-assembly")]
    [TestCase("--baseline-repeat-harness-assembly")]
    [TestCase("--feature-harness-assembly")]
    public void Run_RejectsManifestOutputThatAliasesAnInputFile(string option)
    {
        using var fixture = new AnalyzerFixture();
        string[] arguments = fixture.CreateArguments();
        SetOption(arguments, "--output", GetOption(arguments, option));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(arguments, output, error);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain("aliases an analyzer input"));
    }

    [Test]
    public void FormalOptions_AlwaysUseTheRequiredBootstrapIterationCount()
    {
        using var fixture = new AnalyzerFixture();

        PairedBenchmarkAnalyzerOptions options = PairedBenchmarkAnalyzerOptions.Parse(fixture.CreateArguments());

        Assert.That(options.BootstrapIterations, Is.EqualTo(PairedBenchmarkAnalyzer.DefaultBootstrapIterations));
    }

    [TestCase("--baseline-counters")]
    [TestCase("--baseline-repeat-counters")]
    [TestCase("--feature-counters")]
    [TestCase("--baseline-outputs")]
    [TestCase("--baseline-repeat-outputs")]
    [TestCase("--feature-outputs")]
    [TestCase("--baseline-harness")]
    [TestCase("--feature-harness")]
    public void Run_RejectsManifestOutputInsideAnInputDirectory(string option)
    {
        using var fixture = new AnalyzerFixture();
        string[] arguments = fixture.CreateArguments();
        SetOption(
            arguments,
            "--output",
            Path.Combine(GetOption(arguments, option), "nested-manifest.json"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(arguments, output, error);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain("aliases an analyzer input directory"));
    }

    [TestCase("--baseline-results")]
    [TestCase("--baseline-repeat-results")]
    [TestCase("--feature-results")]
    public void Run_RejectsManifestOutputInsideBenchmarkArtifactRoot(string option)
    {
        using var fixture = new AnalyzerFixture();
        string[] arguments = fixture.CreateArguments();
        string resultParent = Path.GetDirectoryName(GetOption(arguments, option))!;
        SetOption(arguments, "--output", Path.Combine(resultParent, "paired-manifest.json"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(arguments, output, error);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error.ToString(), Does.Contain("aliases an analyzer input directory"));
    }

    [Test]
    public void Run_RejectsPreexistingOutputWithoutTruncatingIt()
    {
        using var fixture = new AnalyzerFixture();
        byte[] original = "preexisting manifest sentinel"u8.ToArray();
        File.WriteAllBytes(fixture.OutputPath, original);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = PairedBenchmarkAnalyzer.Run(fixture.CreateArguments(), output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("already exists"));
            Assert.That(File.ReadAllBytes(fixture.OutputPath), Is.EqualTo(original));
        });
    }

    [TestCase("--feature-results")]
    [TestCase("--feature-harness-provenance")]
    [TestCase("--feature-harness-assembly")]
    public void Analyze_RejectsSymlinkedSnapshots(string option)
    {
        using var fixture = new AnalyzerFixture();
        string target = option switch
        {
            "--feature-results" => fixture.FeatureRun.Results,
            "--feature-harness-provenance" => fixture.FeatureRun.HarnessProvenance,
            "--feature-harness-assembly" => fixture.FeatureRun.HarnessAssembly,
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };
        string link = Path.Combine(
            Path.GetDirectoryName(target)!,
            $"linked-{Guid.NewGuid():N}{Path.GetExtension(target)}");
        if (!TryCreateFileSymbolicLink(link, target))
            Assert.Ignore("Symbolic links are unavailable on this host.");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => fixture.AnalyzeWithFeatureSnapshot(option, link));

        Assert.That(exception!.Message, Does.Contain("symbolic link or reparse point"));
        Assert.That(File.Exists(fixture.OutputPath), Is.False);
    }

    [Test]
    public void Analyze_RejectsSymlinkInsideBenchmarkArtifactRoot()
    {
        using var fixture = new AnalyzerFixture();
        string link = Path.Combine(
            Path.GetDirectoryName(fixture.FeatureRun.Results)!,
            "linked-extra-artifact.txt");
        if (!TryCreateFileSymbolicLink(link, fixture.FeatureRun.Stdout))
            Assert.Ignore("Symbolic links are unavailable on this host.");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("artifact inventory").And.Contain("symbolic link"));
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

    [TestCase(OutputPhase.Setup, NonFiniteComponent.NaN)]
    [TestCase(OutputPhase.Setup, NonFiniteComponent.PositiveInfinity)]
    [TestCase(OutputPhase.Measured, NonFiniteComponent.NaN)]
    [TestCase(OutputPhase.Measured, NonFiniteComponent.PositiveInfinity)]
    public void Analyze_RejectsAuthenticatedNonFiniteBaselineRepeatOutputs(
        OutputPhase phase,
        NonFiniteComponent component)
    {
        using var fixture = new AnalyzerFixture();
        string path = fixture.GetOutputBlobPath(
            AnalyzerRun.BaselineRepeat,
            "ShaderOpacityShader",
            phase);
        byte[] payload = File.ReadAllBytes(path);
        Half value = component == NonFiniteComponent.NaN
            ? Half.NaN
            : Half.PositiveInfinity;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(0, 2),
            BitConverter.HalfToUInt16Bits(value));
        fixture.ReplaceOutputBlob(
            AnalyzerRun.BaselineRepeat,
            "ShaderOpacityShader",
            phase,
            payload,
            updateCounterContract: true);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("non-finite component"));
    }

    [Test]
    public void Analyze_BindsManifestAndArtifactInventoryToTheAnalyzedResultSnapshot()
    {
        using var fixture = new AnalyzerFixture();
        byte[] original = File.ReadAllBytes(fixture.FeatureRun.Results);
        string originalSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        byte[] replacement = fixture.CreateBenchmarkResultMutation(AnalyzerRun.Feature, 79);

        PairedBenchmarkManifest manifest = fixture.AnalyzeWithResultMutation(path =>
        {
            if (string.Equals(path, fixture.FeatureRun.Results, StringComparison.Ordinal))
                File.WriteAllBytes(path, replacement);
        });

        string replacementSha256 = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        Assert.Multiple(() =>
        {
            Assert.That(replacementSha256, Is.Not.EqualTo(originalSha256));
            Assert.That(manifest.Feature.BenchmarkDotNetResultSha256, Is.EqualTo(originalSha256));
            Assert.That(
                manifest.Feature.BenchmarkDotNetArtifactSha256[Path.GetFileName(fixture.FeatureRun.Results)],
                Is.EqualTo(originalSha256));
            Assert.That(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.FeatureRun.Results))).ToLowerInvariant(),
                Is.EqualTo(replacementSha256));
        });
    }

    [Test]
    public void Analyze_BindsManifestAndArtifactInventoryToTheHarnessProvenanceSnapshot()
    {
        using var fixture = new AnalyzerFixture();
        byte[] original = File.ReadAllBytes(fixture.FeatureRun.HarnessProvenance);
        string originalSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        byte[] replacement = "{\"schemaVersion\":1,\"marker\":\"replacement\"}\n"u8.ToArray();

        PairedBenchmarkManifest manifest = fixture.AnalyzeWithProvenanceMutation(path =>
        {
            if (string.Equals(path, fixture.FeatureRun.HarnessProvenance, StringComparison.Ordinal))
                File.WriteAllBytes(path, replacement);
        });

        string replacementSha256 = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        Assert.Multiple(() =>
        {
            Assert.That(replacementSha256, Is.Not.EqualTo(originalSha256));
            Assert.That(manifest.Feature.HarnessProvenanceSha256, Is.EqualTo(originalSha256));
            Assert.That(
                manifest.Feature.BenchmarkDotNetArtifactSha256[
                    Path.GetFileName(fixture.FeatureRun.HarnessProvenance)],
                Is.EqualTo(originalSha256));
            Assert.That(Sha256File(fixture.FeatureRun.HarnessProvenance), Is.EqualTo(replacementSha256));
        });
    }

    [Test]
    public void Analyze_BindsManifestAndArtifactInventoryToTheExecutedHarnessAssemblySnapshot()
    {
        using var fixture = new AnalyzerFixture();
        byte[] original = File.ReadAllBytes(fixture.FeatureRun.HarnessAssembly);
        string originalSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        byte[] replacement = "replacement harness assembly bytes"u8.ToArray();

        PairedBenchmarkManifest manifest = fixture.AnalyzeWithAssemblyMutation(path =>
        {
            if (string.Equals(path, fixture.FeatureRun.HarnessAssembly, StringComparison.Ordinal))
                File.WriteAllBytes(path, replacement);
        });

        string replacementSha256 = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        Assert.Multiple(() =>
        {
            Assert.That(replacementSha256, Is.Not.EqualTo(originalSha256));
            Assert.That(manifest.Feature.ExecutedHarnessAssemblySha256, Is.EqualTo(originalSha256));
            Assert.That(
                manifest.Feature.BenchmarkDotNetArtifactSha256[
                    Path.GetFileName(fixture.FeatureRun.HarnessAssembly)],
                Is.EqualTo(originalSha256));
            Assert.That(Sha256File(fixture.FeatureRun.HarnessAssembly), Is.EqualTo(replacementSha256));
        });
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

    [TestCase(OutputPhase.Setup, LocalizedDefect.Rgb)]
    [TestCase(OutputPhase.Setup, LocalizedDefect.Alpha)]
    [TestCase(OutputPhase.Setup, LocalizedDefect.GridBoundary)]
    [TestCase(OutputPhase.Measured, LocalizedDefect.Rgb)]
    [TestCase(OutputPhase.Measured, LocalizedDefect.Alpha)]
    [TestCase(OutputPhase.Measured, LocalizedDefect.GridBoundary)]
    public void Analyze_RejectsLocalizedOutputDefects(
        OutputPhase phase,
        LocalizedDefect defect)
    {
        using var fixture = new AnalyzerFixture();
        fixture.IntroduceLocalizedDefect(phase, defect);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("localized 16x16 parity gate")
                .And.Contain("ParameterOnlyAnimation")
                .And.Contain(phase.ToString()).IgnoreCase);
    }

    [TestCase(OutputPhase.Setup)]
    [TestCase(OutputPhase.Measured)]
    public void Analyze_RejectsSparseCheckerboardDefectCenteredOnWindowBoundary(OutputPhase phase)
    {
        using var fixture = new AnalyzerFixture();
        fixture.IntroduceLocalizedDefect(phase, LocalizedDefect.SparseGridBoundary);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("localized 16x16 parity gate")
                .And.Contain("ParameterOnlyAnimation")
                .And.Contain(phase.ToString()).IgnoreCase);
    }

    [TestCase(OutputPhase.Setup)]
    [TestCase(OutputPhase.Measured)]
    public void Analyze_RejectsTailAnchoredAlphaDefect(OutputPhase phase)
    {
        using var fixture = new AnalyzerFixture();
        fixture.IntroduceTailAnchoredAlphaDefect(phase);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("localized 16x16 parity gate")
                .And.Contain("SmallObjectFixedOverhead")
                .And.Contain(phase.ToString()).IgnoreCase);
    }

    [Test]
    public void Analyze_AcceptsSingleTailPixelInFullLocalizedWindow()
    {
        using var fixture = new AnalyzerFixture();
        fixture.IntroduceStaticTailPixelDefect();

        Assert.That(() => fixture.Analyze(), Throws.Nothing);
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
    public void Analyze_RejectsIdenticalBaselineAndFeatureRevisions()
    {
        using var fixture = new AnalyzerFixture();

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => fixture.AnalyzeSameRevision());

        Assert.That(exception!.Message, Does.Contain("distinct revisions"));
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
    public void BenchmarkHarnessProvenance_EmbedsFeatureSourcesAndInheritedBuildInputs()
    {
        SortedDictionary<string, string> inputs = BenchmarkHarnessProvenance.ReadBuildInputSha256(
            typeof(PairedBenchmarkAnalyzer).Assembly);
        string harnessPath = Path.Combine(
            GpuPassFusionEvidencePaths.Discover().RepositoryRoot,
            "tests",
            "Beutl.Benchmarks");
        HarnessAssemblyMetadata resolved = PairedBenchmarkAnalyzer.ReadHarnessAssemblyMetadataForTest(
            File.ReadAllBytes(typeof(PairedBenchmarkAnalyzer).Assembly.Location));
        PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
            harnessPath,
            resolved.SourceRevision,
            resolved.BuildInputSha256);

        Assert.Multiple(() =>
        {
            Assert.That(inputs.Keys, Does.Contain("Directory.Build.props"));
            Assert.That(inputs.Keys, Does.Contain("Directory.Build.targets"));
            Assert.That(inputs.Keys, Does.Contain("Directory.Packages.props"));
            Assert.That(inputs.Keys, Does.Contain("global.json"));
            Assert.That(inputs.Keys, Does.Contain("tests/Directory.Build.props"));
            Assert.That(inputs.Keys, Does.Contain("tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj"));
            Assert.That(
                inputs.Keys,
                Does.Contain("tests/Beutl.Benchmarks/Rendering/BenchmarkHarnessProvenance.targets"));
            Assert.That(inputs.Keys, Has.None.Contains("/obj/"));
            Assert.That(inputs, Is.EqualTo(resolved.BuildInputSha256));
            Assert.That(
                resolved.SourceRevision,
                Is.EqualTo(BenchmarkHarnessProvenance.ExtractSourceRevision(
                    typeof(PairedBenchmarkAnalyzer).Assembly
                        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
                        .InformationalVersion)));
        });
    }

    [Test]
    [NonParallelizable]
    public void BenchmarkHarnessProvenance_BindsBuildInputsToTheExecutingAssemblyBytes()
    {
        const string variable = "BEUTL_RENDER_BENCHMARK_HARNESS_PROVENANCE";
        string? original = Environment.GetEnvironmentVariable(variable);
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-harness-provenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "provenance.json");
        try
        {
            Environment.SetEnvironmentVariable(variable, path);

            BenchmarkHarnessProvenance.WriteFromEnvironment(typeof(PairedBenchmarkAnalyzer).Assembly);

            JsonObject root = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            var buildInputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach ((string name, JsonNode? value) in root["buildInputSha256"]!.AsObject())
                buildInputs.Add(name, value!.GetValue<string>());
            Assert.Multiple(() =>
            {
                Assert.That(root["schemaVersion"]!.GetValue<int>(), Is.EqualTo(2));
                Assert.That(
                    root["harnessAssemblySha256"]!.GetValue<string>(),
                    Is.EqualTo(Convert.ToHexString(SHA256.HashData(
                        File.ReadAllBytes(typeof(PairedBenchmarkAnalyzer).Assembly.Location))).ToLowerInvariant()));
                Assert.That(
                    root["buildInputBundleSha256"]!.GetValue<string>(),
                    Is.EqualTo(BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(buildInputs)));
                Assert.That(
                    root["sourceRevision"]!.GetValue<string>(),
                    Does.Match("^[0-9a-f]{40}$"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void HarnessAssemblyMetadata_ParsesCurrentAssemblyWithoutLoadingAnotherCopy()
    {
        byte[] bytes = File.ReadAllBytes(typeof(PairedBenchmarkAnalyzer).Assembly.Location);

        HarnessAssemblyMetadata metadata = PairedBenchmarkAnalyzer.ReadHarnessAssemblyMetadataForTest(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.AssemblyName, Is.EqualTo("Beutl.Benchmarks"));
            Assert.That(metadata.InformationalVersion, Does.Contain(metadata.SourceRevision));
            Assert.That(metadata.SourceRevision, Does.Match("^[0-9a-f]{40}$"));
            Assert.That(
                metadata.BuildInputSha256,
                Is.EqualTo(BenchmarkHarnessProvenance.ReadBuildInputSha256(
                    typeof(PairedBenchmarkAnalyzer).Assembly)));
            Assert.That(
                metadata.BuildInputBundleSha256,
                Is.EqualTo(BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(
                    metadata.BuildInputSha256)));
        });
    }

    [Test]
    public void HarnessAssemblyMetadata_RejectsDuplicateBuildInputMetadata()
    {
        using var repository = new HarnessRepositoryFixture();
        byte[] assembly = repository.BuildHarnessAssembly(
            HarnessKind.Feature,
            duplicateBuildInputMetadata: true);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => PairedBenchmarkAnalyzer.ReadHarnessAssemblyMetadataForTest(assembly));

        Assert.That(exception!.Message, Does.Contain("duplicate build-input metadata"));
    }

    [Test]
    public void Analyze_RejectsPlainTextHarnessAssemblySnapshot()
    {
        using var fixture = new AnalyzerFixture();

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => fixture.AnalyzeWithoutAssemblyMetadataOverride());

        Assert.That(exception!.Message, Does.Contain("valid managed PE image"));
    }

    [Test]
    public void Analyze_RejectsCapturedAssemblyMetadataWithUnexpectedIdentity()
    {
        using var fixture = new AnalyzerFixture();
        SortedDictionary<string, string> inputs = BenchmarkHarnessProvenance.ReadBuildInputSha256(
            typeof(PairedBenchmarkAnalyzer).Assembly);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            fixture.AnalyzeWithAssemblyMetadataReader((path, _) => new HarnessAssemblyMetadata(
                path == fixture.FeatureRun.HarnessAssembly
                    ? "Unexpected.Harness"
                    : "Beutl.GpuPassTargetBenchmarkHarness",
                $"2.99.99+{AnalyzerFixture.FeatureSha}",
                AnalyzerFixture.FeatureSha,
                BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(inputs),
                new SortedDictionary<string, string>(inputs, StringComparer.Ordinal))));

        Assert.That(exception!.Message, Does.Contain("harness identity mismatch"));
    }

    [Test]
    public void Analyze_RejectsLegacyHarnessProvenanceWithoutBuildTimeAttestation()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateHarnessProvenance(
            AnalyzerRun.Baseline,
            root => root["schemaVersion"] = 1);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("schema 2 build-time attestation is required"));
    }

    [Test]
    public void Analyze_RejectsHarnessAttestationThatDoesNotMatchTrackedInputs()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateHarnessBuildInput(AnalyzerRun.Feature);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("captured PE assembly metadata"));
    }

    [Test]
    public void Analyze_RejectsSyntheticHarnessAttestationThatDoesNotMatchCapturedAssembly()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateHarnessProvenance(
            AnalyzerRun.Feature,
            root => root["harnessAssemblySha256"] = AnalyzerFixture.AlternateSha256);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(
            exception!.Message,
            Does.Contain("independently captured executed assembly snapshot"));
    }

    [Test]
    public void Analyze_RejectsBaselineRepeatProducedByDifferentCapturedHarnessAssembly()
    {
        using var fixture = new AnalyzerFixture();
        fixture.ReplaceHarnessAssembly(
            AnalyzerRun.BaselineRepeat,
            "different authenticated target harness assembly"u8.ToArray(),
            updateProvenance: true);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("same authenticated harness executable"));
    }

    [Test]
    public void Analyze_RejectsHarnessRepositoryHeadThatDoesNotMatchFeatureRevision()
    {
        using var fixture = new AnalyzerFixture();

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => fixture.AnalyzeWithHarnessRepositoryHead(AnalyzerFixture.AlternateRevision));

        Assert.That(exception!.Message, Does.Contain("repository HEAD").And.Contain("feature revision"));
    }

    [Test]
    public void HarnessBuildInputs_IncludeApplicableTargetAndFeatureMsBuildConfiguration()
    {
        using var repository = new HarnessRepositoryFixture();

        HarnessAssemblyMetadata targetSnapshot = repository.BuildHarness(HarnessKind.Target);
        HarnessAssemblyMetadata featureSnapshot = repository.BuildHarness(HarnessKind.Feature);
        SortedDictionary<string, string> target = targetSnapshot.BuildInputSha256;
        SortedDictionary<string, string> feature = featureSnapshot.BuildInputSha256;
        PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
            repository.TargetHarnessPath,
            repository.Head,
            target);
        PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
            repository.FeatureHarnessPath,
            repository.Head,
            feature);

        Assert.Multiple(() =>
        {
            Assert.That(target.Keys, Does.Contain("Directory.Build.props"));
            Assert.That(target.Keys, Does.Contain("Directory.Build.targets"));
            Assert.That(target.Keys, Does.Contain("Directory.Packages.props"));
            Assert.That(target.Keys, Does.Contain("global.json"));
            Assert.That(target.Keys, Does.Contain("build/Shared.targets"));
            Assert.That(target.Keys, Does.Contain("build/Nested.targets"));
            Assert.That(target.Keys, Does.Contain("build/BenchmarkHarnessProvenance.targets"));
            Assert.That(target.Keys, Does.Contain("tests/feature/Shared.cs"));
            Assert.That(target.Keys, Does.Not.Contain("tests/Directory.Build.props"));
            Assert.That(feature.Keys, Does.Contain("Directory.Build.props"));
            Assert.That(feature.Keys, Does.Contain("Directory.Build.targets"));
            Assert.That(feature.Keys, Does.Contain("Directory.Packages.props"));
            Assert.That(feature.Keys, Does.Contain("global.json"));
            Assert.That(feature.Keys, Does.Contain("tests/Directory.Build.props"));
            Assert.That(feature.Keys, Does.Contain("build/Shared.targets"));
            Assert.That(feature.Keys, Does.Contain("build/Nested.targets"));
            Assert.That(feature.Keys, Does.Contain("build/BenchmarkHarnessProvenance.targets"));
        });
    }

    [TestCase("Directory.Build.props", HarnessKind.Target)]
    [TestCase("Directory.Packages.props", HarnessKind.Target)]
    [TestCase("tests/Directory.Build.props", HarnessKind.Feature)]
    public void HarnessBuildInputs_RejectDirtyInheritedConfiguration(
        string relativePath,
        HarnessKind harness)
    {
        using var repository = new HarnessRepositoryFixture();
        HarnessAssemblyMetadata metadata = repository.BuildHarness(harness);
        repository.Dirty(relativePath);
        string path = harness == HarnessKind.Target
            ? repository.TargetHarnessPath
            : repository.FeatureHarnessPath;

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
                path,
                repository.Head,
                metadata.BuildInputSha256));

        Assert.That(
            exception!.Message,
            Does.Contain("clean tracked repository").And.Contain(relativePath));
    }

    [TestCase("Directory.Build.props", HarnessKind.Target)]
    [TestCase("tests/feature/Program.cs", HarnessKind.Feature)]
    public void HarnessBuildInputs_RejectHiddenDirtyInputs(
        string relativePath,
        HarnessKind harness)
    {
        using var repository = new HarnessRepositoryFixture();
        HarnessAssemblyMetadata metadata = repository.BuildHarness(harness);
        repository.AssumeUnchangedAndDirty(relativePath);
        string path = harness == HarnessKind.Target
            ? repository.TargetHarnessPath
            : repository.FeatureHarnessPath;

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
                path,
                repository.Head,
                metadata.BuildInputSha256));

        Assert.That(exception!.Message, Does.Contain("special index state").And.Contain(relativePath));
    }

    [Test]
    public void HarnessBuildInputs_RejectRepositoryHeadThatDoesNotMatchClaimedRevision()
    {
        using var repository = new HarnessRepositoryFixture();
        HarnessAssemblyMetadata metadata = repository.BuildHarness(HarnessKind.Target);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
                repository.TargetHarnessPath,
                AnalyzerFixture.AlternateRevision,
                metadata.BuildInputSha256));

        Assert.That(exception!.Message, Does.Contain("repository HEAD").And.Contain("feature revision"));
    }

    [TestCase("tests/feature/Program.cs")]
    [TestCase("build/BenchmarkHarnessProvenance.targets")]
    public void HarnessBuildInputs_RejectOmittedMandatoryAnchors(string relativePath)
    {
        using var repository = new HarnessRepositoryFixture();
        HarnessAssemblyMetadata metadata = repository.BuildHarness(HarnessKind.Feature);
        var incomplete = new SortedDictionary<string, string>(
            metadata.BuildInputSha256,
            StringComparer.Ordinal);
        Assert.That(incomplete.Remove(relativePath), Is.True);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
                repository.FeatureHarnessPath,
                repository.Head,
                incomplete));

        Assert.That(exception!.Message, Does.Contain("omits mandatory"));
    }

    [Test]
    public void HarnessBuildInputs_RejectTrackedSymbolicLinks()
    {
        using var repository = new HarnessRepositoryFixture();
        HarnessAssemblyMetadata metadata = repository.BuildHarness(HarnessKind.Feature);
        const string relativePath = "tests/feature/LinkedInput.cs";
        if (!repository.TryAddTrackedSymbolicLink(relativePath, "Program.cs"))
            Assert.Ignore("Symbolic links are unavailable on this host.");
        var inputs = new SortedDictionary<string, string>(
            metadata.BuildInputSha256,
            StringComparer.Ordinal)
        {
            [relativePath] = Sha256File(Path.Combine(repository.Root, relativePath)),
        };

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            PairedBenchmarkAnalyzer.VerifyHarnessBuildInputsForTest(
                repository.FeatureHarnessPath,
                repository.Head,
                inputs));

        Assert.That(exception!.Message, Does.Contain("symbolic link").And.Contain(relativePath));
    }

    [TestCase("../outside.rgba16f")]
    [TestCase("nested/output.rgba16f")]
    [TestCase("nested\\output.rgba16f")]
    [TestCase("/tmp/output.rgba16f")]
    [TestCase("manifest.json")]
    public void FeatureVisualExporter_RejectsUnsafeArtifactNames(string name)
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "beutl-feature-artifacts");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => FeatureVisualEvidenceExporter.ResolveArtifactPathForTest(outputDirectory, name));

        Assert.That(exception!.Message, Does.Contain("artifact").And.Contain("unsafe"));
    }

    [Test]
    public void FeatureVisualExporter_ParsesAndHashesOneManifestSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-feature-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "manifest.json");
        byte[] bytes = "{\"schemaVersion\":1,\"marker\":\"original\"}"u8.ToArray();
        File.WriteAllBytes(path, bytes);
        try
        {
            FeatureBaselineManifestSnapshot snapshot =
                FeatureVisualEvidenceExporter.LoadManifestSnapshotForTest(path);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Manifest["marker"]!.GetValue<string>(), Is.EqualTo("original"));
                Assert.That(
                    snapshot.Sha256,
                    Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public void FeatureVisualExporter_ResolvesRevisionFromItsExecutingAssemblyOutsideARepository()
    {
        string original = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), $"beutl-feature-revision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Environment.CurrentDirectory = root;

            string revision = FeatureVisualEvidenceExporter.CurrentCodeShaForTest();

            Assert.That(revision, Does.Match("^[0-9a-f]{40}$"));
        }
        finally
        {
            Environment.CurrentDirectory = original;
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void PairedRunner_RejectsHarnessesThatDoNotAuthenticateTheExecutedBinary()
    {
        GpuPassFusionEvidenceStackSliceGate.RequireStack4EvidenceSlice();
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
            Assert.That(
                runner,
                Does.Contain("--feature-harness \"$feature_worktree/tests/Beutl.Benchmarks\""));
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

    [Test]
    public void Analyze_RejectsNonCanonicalOutputBlobMapping()
    {
        using var fixture = new AnalyzerFixture();
        const string caseName = "SingleShader";
        string canonical = fixture.GetOutputBlobPath(AnalyzerRun.Baseline, caseName, OutputPhase.Setup);
        const string alias = "SingleShader.setup-copy.rgba16f";
        File.Move(canonical, Path.Combine(Path.GetDirectoryName(canonical)!, alias));
        fixture.MutateCounter(
            AnalyzerRun.Baseline,
            caseName,
            root => root["setupOutputBlobFile"] = alias);

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("canonical").IgnoreCase);
    }

    [Test]
    public void Analyze_RejectsDuplicateOutputBlobMapping()
    {
        using var fixture = new AnalyzerFixture();
        fixture.MutateCounter(
            AnalyzerRun.Baseline,
            "SingleShader",
            root => root["setupOutputBlobFile"] = "NoEffectControl.setup.rgba16f");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("canonical").IgnoreCase);
    }

    [Test]
    public void Analyze_RejectsUnreferencedOutputBlobFile()
    {
        using var fixture = new AnalyzerFixture();
        string source = fixture.GetOutputBlobPath(
            AnalyzerRun.Feature,
            "SingleShader",
            OutputPhase.Setup);
        File.Copy(source, Path.Combine(Path.GetDirectoryName(source)!, "unreferenced.rgba16f"));

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() => fixture.Analyze());

        Assert.That(exception!.Message, Does.Contain("unreferenced").IgnoreCase);
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

    [Test]
    public void MixedSpatialColor_UsesARealSpatialEffect()
    {
        FilterEffect effect = RenderPipelineBenchmarkSession.CreateMixedSpatialEffectForTest();

        Assert.That(effect, Is.TypeOf<Blur>());
        Assert.That(((Blur)effect).Sigma.CurrentValue, Is.EqualTo(new Size(3, 3)));
    }

    [Test]
    public void TargetHarness_MixedSpatialColorUsesTheMatchingBlurStage()
    {
        string source = File.ReadAllText(Path.Combine(
            GpuPassFusionEvidencePaths.Discover().EvidenceDirectory,
            "target-benchmark-harness",
            "TargetRenderPipelineBenchmarks.cs"));
        int methodStart = source.IndexOf(
            "private static TargetSceneFixture Mixed(",
            StringComparison.Ordinal);
        int methodEnd = source.IndexOf(
            "private static TargetSceneFixture Small(",
            methodStart,
            StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));
        string method = source[methodStart..methodEnd];
        int wrapEffectStart = source.IndexOf(
            "public RenderNode WrapEffect(",
            StringComparison.Ordinal);
        int wrapEffectEnd = source.IndexOf(
            "public FilterEffect.Resource CreateShaderResource(",
            wrapEffectStart,
            StringComparison.Ordinal);
        Assert.That(wrapEffectStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(wrapEffectEnd, Is.GreaterThan(wrapEffectStart));
        string wrapEffect = source[wrapEffectStart..wrapEffectEnd];

        Assert.Multiple(() =>
        {
            AssertTokensInOrder(
                method,
                "fixture.WrapShader(Source(scene, s_domain), TargetShaderScript.Create(TargetShaderKind.Gamma))",
                "var blur = new Blur();",
                "blur.Sigma.CurrentValue = new Size(3, 3);",
                "current = fixture.WrapEffect(current, blur);",
                "fixture.WrapShader(current, TargetShaderScript.Create(TargetShaderKind.Invert))",
                "var opacity = new OpacityRenderNode(0.8f);",
                "opacity.AddChild(current);",
                "fixture.WrapShader(opacity, TargetShaderScript.Create(TargetShaderKind.ChannelRotate))");
            AssertTokensInOrder(
                wrapEffect,
                "effect.ToResource(CompositionContext.Default)",
                "_resources.Add(resource);",
                "new FilterEffectRenderNode(resource)",
                "node.AddChild(input);");
            Assert.That(method, Does.Not.Contain("TargetShaderKind.WholeSourceIdentity"));
            Assert.That(source, Does.Contain(
                "private static readonly Rect s_domain = TargetRenderPipelineScenes.TargetDomain;"));
            Assert.That(source, Does.Contain(
                "bounds = bounds.Intersect(TargetRenderPipelineScenes.TargetDomain);"));
        });
    }

    [Test]
    public void SetupRenderPlan_RetainsTheOriginalFifthRequestWithoutAddingAnotherRender()
    {
        IReadOnlyList<(int Frame, bool RetainRasterization)> plan =
            RenderPipelineBenchmarkSession.GetSetupRenderPlanForTest("ParameterOnlyAnimation");

        Assert.Multiple(() =>
        {
            Assert.That(plan, Has.Count.EqualTo(RenderPipelineBenchmarkConfig.SetupWarmupFrameCount));
            Assert.That(plan.Count(static item => item.RetainRasterization), Is.EqualTo(1));
            Assert.That(plan[^1].RetainRasterization, Is.True);
            Assert.That(plan.Select(static item => item.Frame), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        });
    }

    private static string GetOption(string[] arguments, string option)
    {
        int index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : throw new InvalidOperationException($"Missing test argument: {option}");
    }

    private static void SetOption(string[] arguments, string option, string value)
    {
        int index = Array.IndexOf(arguments, option);
        if (index < 0 || index + 1 >= arguments.Length)
            throw new InvalidOperationException($"Missing test argument: {option}");
        arguments[index + 1] = value;
    }

    private static string Sha256File(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException)
        {
            return false;
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

    private static void AssertTokensInOrder(string source, params string[] tokens)
    {
        int startIndex = 0;
        foreach (string token in tokens)
        {
            int index = source.IndexOf(token, startIndex, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(startIndex), $"Missing or out-of-order token: {token}");
            startIndex = index + token.Length;
        }
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

    public enum LocalizedDefect
    {
        Rgb,
        Alpha,
        GridBoundary,
        SparseGridBoundary,
    }

    public enum NonFiniteComponent
    {
        NaN,
        PositiveInfinity,
    }

    public enum HarnessKind
    {
        Target,
        Feature,
    }

    private sealed class HarnessRepositoryFixture : IDisposable
    {
        public HarnessRepositoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"beutl-harness-inputs-{Guid.NewGuid():N}");
            TargetHarnessPath = Path.Combine(Root, "target");
            FeatureHarnessPath = Path.Combine(Root, "tests", "feature");
            Directory.CreateDirectory(TargetHarnessPath);
            Directory.CreateDirectory(FeatureHarnessPath);
            Directory.CreateDirectory(Path.Combine(Root, "build"));
            Write(
                "Directory.Build.props",
                """
                <Project>
                  <PropertyGroup>
                    <RootDirectory>$(MSBuildThisFileDirectory)</RootDirectory>
                  </PropertyGroup>
                </Project>
                """ + "\n");
            Write("Directory.Build.targets", "<Project />\n");
            Write("Directory.Packages.props", "<Project />\n");
            Write("global.json", "{\"sdk\":{\"version\":\"10.0.301\"}}\n");
            Write(".gitignore", "**/bin/\n**/obj/\n");
            Write(
                "tests/Directory.Build.props",
                """
                <Project>
                  <Import Project="../Directory.Build.props" />
                </Project>
                """ + "\n");
            Write(
                "build/BenchmarkHarnessProvenance.targets",
                File.ReadAllText(Path.Combine(
                    GpuPassFusionEvidencePaths.Discover().RepositoryRoot,
                    "tests",
                    "Beutl.Benchmarks",
                    "Rendering",
                    "BenchmarkHarnessProvenance.targets")));
            Write(
                "build/Nested.targets",
                """
                <Project>
                  <Import Project="$(MSBuildThisFileDirectory)BenchmarkHarnessProvenance.targets" />
                </Project>
                """ + "\n");
            Write(
                "build/Shared.targets",
                """
                <Project>
                  <Import Project="$(MSBuildThisFileDirectory)Nested.targets" />
                </Project>
                """ + "\n");
            Write("tests/feature/Program.cs", "internal static class FeatureProgram { }\n");
            Write("tests/feature/Shared.cs", "internal static class SharedInput { }\n");
            Write("tests/feature/FeatureBenchmark.cs", "internal static class FeatureBenchmark { }\n");
            Write(
                "tests/feature/BenchmarkHarnessProvenance.cs",
                "internal static class FeatureBenchmarkHarnessProvenanceAnchor { }\n");
            Write(
                "tests/feature/Feature.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Feature.Harness</AssemblyName>
                    <InformationalVersion>1.0.0+1111111111111111111111111111111111111111</InformationalVersion>
                    <HarnessSharedTargets>$(MSBuildProjectDirectory)/../../build/Shared.targets</HarnessSharedTargets>
                  </PropertyGroup>
                  <ItemGroup>
                    <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute"
                                       Condition="'$(DuplicateHarnessMetadata)' == 'true'">
                      <_Parameter1>BeutlBenchmarkHarnessBuildInputSha256</_Parameter1>
                      <_Parameter2>tests/feature/Program.cs|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</_Parameter2>
                    </AssemblyAttribute>
                  </ItemGroup>
                  <Import Project="$(HarnessSharedTargets)" />
                </Project>
                """ + "\n");
            Write("engine/Engine.cs", "public static class EngineInput { }\n");
            Write(
                "engine/Engine.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """ + "\n");
            Write("target/Program.cs", "internal static class TargetProgram { }\n");
            Write("target/TargetBenchmark.cs", "internal static class TargetBenchmark { }\n");
            Write(
                "target/BenchmarkHarnessProvenance.cs",
                "internal static class TargetBenchmarkHarnessProvenanceAnchor { }\n");
            Write(
                "target/Target.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Target.Harness</AssemblyName>
                    <InformationalVersion>1.0.0+1111111111111111111111111111111111111111</InformationalVersion>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="../tests/feature/Shared.cs" Link="Shared.cs" />
                    <ProjectReference Include="$(BaselineEngineProject)" />
                  </ItemGroup>
                  <PropertyGroup>
                    <HarnessSharedTargets>$(MSBuildProjectDirectory)/../build/Shared.targets</HarnessSharedTargets>
                  </PropertyGroup>
                  <Import Project="$(HarnessSharedTargets)" />
                </Project>
                """ + "\n");
            RunGit("init");
            RunGit("add", ".");
            RunGit(
                "-c",
                "user.name=Beutl Tests",
                "-c",
                "user.email=tests@beutl.invalid",
                "-c",
                "commit.gpgSign=false",
                "commit",
                "-m",
                "test fixture");
            Head = RunProcess("git", "rev-parse", "HEAD").Trim();
        }

        public string Root { get; }

        public string TargetHarnessPath { get; }

        public string FeatureHarnessPath { get; }

        public string Head { get; private set; }

        public void Dirty(string relativePath)
            => File.AppendAllText(Path.Combine(Root, relativePath), "<!-- dirty -->\n", new UTF8Encoding(false));

        public void AssumeUnchangedAndDirty(string relativePath)
        {
            RunGit("update-index", "--assume-unchanged", "--", relativePath);
            Dirty(relativePath);
        }

        public HarnessAssemblyMetadata BuildHarness(HarnessKind harness)
            => PairedBenchmarkAnalyzer.ReadHarnessAssemblyMetadataForTest(
                BuildHarnessAssembly(harness, duplicateBuildInputMetadata: false));

        public byte[] BuildHarnessAssembly(
            HarnessKind harness,
            bool duplicateBuildInputMetadata)
        {
            string harnessPath = harness == HarnessKind.Target ? TargetHarnessPath : FeatureHarnessPath;
            string project = Path.Combine(
                harnessPath,
                harness == HarnessKind.Target ? "Target.csproj" : "Feature.csproj");
            var arguments = new List<string>
            {
                "build",
                project,
                "-c",
                "Release",
                "--nologo",
                "-m:1",
            };
            if (harness == HarnessKind.Target)
            {
                arguments.Add(
                    $"-p:BaselineEngineProject={Path.Combine(Root, "engine", "Engine.csproj")}");
            }
            if (duplicateBuildInputMetadata)
                arguments.Add("-p:DuplicateHarnessMetadata=true");
            _ = RunProcess("dotnet", arguments.ToArray());
            string assembly = Path.Combine(
                harnessPath,
                "bin",
                "Release",
                "net10.0",
                harness == HarnessKind.Target ? "Target.Harness.dll" : "Feature.Harness.dll");
            return File.ReadAllBytes(assembly);
        }

        public bool TryAddTrackedSymbolicLink(string relativePath, string target)
        {
            string path = Path.Combine(Root, relativePath);
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or PlatformNotSupportedException)
            {
                return false;
            }
            RunGit("add", "--", relativePath);
            RunGit(
                "-c",
                "user.name=Beutl Tests",
                "-c",
                "user.email=tests@beutl.invalid",
                "-c",
                "commit.gpgSign=false",
                "commit",
                "-m",
                "add symlink fixture");
            Head = RunProcess("git", "rev-parse", "HEAD").Trim();
            return true;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private void Write(string relativePath, string contents)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }

        private void RunGit(params string[] arguments)
            => _ = RunProcess("git", arguments);

        private string RunProcess(string fileName, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start git for harness-input fixture.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Fixture command failed ({fileName} {string.Join(' ', arguments)}): {stderr}{stdout}");
            }
            return stdout;
        }
    }

    private sealed class AnalyzerFixture : IDisposable
    {
        public const string AlternateSha256 =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        public const string AlternateChecksum = "dddddddddddddddd";

        internal const string BaselineSha = "43a38e665d9bf52548161a3917e748bd1457ff55";
        internal const string FeatureSha = "1111111111111111111111111111111111111111";
        internal const string AlternateRevision = "3333333333333333333333333333333333333333";
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
        private static readonly Lazy<SortedDictionary<string, string>> s_baselineHarnessBuildInputs =
            new(() => BenchmarkHarnessProvenance.ReadBuildInputSha256(
                typeof(PairedBenchmarkAnalyzer).Assembly));
        private static readonly Lazy<SortedDictionary<string, string>> s_featureHarnessBuildInputs =
            new(() => BenchmarkHarnessProvenance.ReadBuildInputSha256(
                typeof(PairedBenchmarkAnalyzer).Assembly));

        private readonly Dictionary<AnalyzerRun, RunPaths> _runs;

        private static string ResolveBaselineHarnessPath()
        {
            GpuPassFusionEvidencePaths paths = GpuPassFusionEvidencePaths.Discover();
            string targetHarness = Path.Combine(paths.EvidenceDirectory, "target-benchmark-harness");
            return GpuPassFusionEvidenceStackSliceGate.HasStack4EvidenceSlice
                ? targetHarness
                : ResolveFeatureHarnessPath();
        }

        public AnalyzerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"beutl-paired-analyzer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            _runs = new Dictionary<AnalyzerRun, RunPaths>
            {
                [AnalyzerRun.Baseline] = CreateRun(
                    "baseline-a",
                    BaselineSha,
                    100,
                    "Beutl.GpuPassTargetBenchmarkHarness",
                    s_baselineHarnessBuildInputs.Value,
                    "synthetic target harness assembly"u8.ToArray()),
                [AnalyzerRun.BaselineRepeat] = CreateRun(
                    "baseline-b",
                    BaselineSha,
                    100,
                    "Beutl.GpuPassTargetBenchmarkHarness",
                    s_baselineHarnessBuildInputs.Value,
                    "synthetic target harness assembly"u8.ToArray()),
                [AnalyzerRun.Feature] = CreateRun(
                    "feature",
                    FeatureSha,
                    100,
                    "Beutl.Benchmarks",
                    s_featureHarnessBuildInputs.Value,
                    "synthetic feature harness assembly"u8.ToArray()),
            };
            SetSamples(AnalyzerRun.Feature, "ShaderOpacityShader", 80);
            OutputPath = Path.Combine(Root, "paired-manifest.json");
        }

        public string Root { get; }

        public string OutputPath { get; }

        public RunPaths FeatureRun => _runs[AnalyzerRun.Feature];

        public PairedBenchmarkManifest Analyze()
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions());

        public PairedBenchmarkManifest AnalyzeSameRevision()
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(featureSha: BaselineSha));

        public PairedBenchmarkManifest AnalyzeWithHarnessRepositoryHead(string repositoryHead)
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(harnessRepositoryHead: repositoryHead));

        public PairedBenchmarkManifest AnalyzeWithoutAssemblyMetadataOverride()
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(useSyntheticAssemblyMetadata: false));

        public PairedBenchmarkManifest AnalyzeWithAssemblyMetadataReader(
            Func<string, byte[], HarnessAssemblyMetadata> reader)
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(assemblyMetadataReader: reader));

        public PairedBenchmarkManifest AnalyzeWithFeatureSnapshot(string option, string path)
            => option switch
            {
                "--feature-results" => PairedBenchmarkAnalyzer.Analyze(
                    CreateOptions(featureResultsPath: path)),
                "--feature-harness-provenance" => PairedBenchmarkAnalyzer.Analyze(
                    CreateOptions(featureHarnessProvenancePath: path)),
                "--feature-harness-assembly" => PairedBenchmarkAnalyzer.Analyze(
                    CreateOptions(featureHarnessAssemblyPath: path)),
                _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
            };

        public int Run(TextWriter output, TextWriter error)
            => PairedBenchmarkAnalyzer.Run(CreateOptions(), output, error);

        public PairedBenchmarkManifest AnalyzeWithResultMutation(Action<string> callback)
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(resultSnapshotCaptured: callback));

        public PairedBenchmarkManifest AnalyzeWithProvenanceMutation(Action<string> callback)
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(provenanceSnapshotCaptured: callback));

        public PairedBenchmarkManifest AnalyzeWithAssemblyMutation(Action<string> callback)
            => PairedBenchmarkAnalyzer.Analyze(CreateOptions(assemblySnapshotCaptured: callback));

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
                "--baseline-harness-provenance", baseline.HarnessProvenance,
                "--baseline-repeat-harness-provenance", repeat.HarnessProvenance,
                "--feature-harness-provenance", feature.HarnessProvenance,
                "--baseline-harness-assembly", baseline.HarnessAssembly,
                "--baseline-repeat-harness-assembly", repeat.HarnessAssembly,
                "--feature-harness-assembly", feature.HarnessAssembly,
                "--baseline-outputs", baseline.OutputBlobs,
                "--baseline-repeat-outputs", repeat.OutputBlobs,
                "--feature-outputs", feature.OutputBlobs,
                "--baseline-sha", BaselineSha,
                "--feature-sha", FeatureSha,
                "--baseline-command", "synthetic baseline A",
                "--baseline-repeat-command", "synthetic baseline B",
                "--feature-command", "synthetic feature",
                "--runner-sha256", RunnerSha256,
                "--baseline-harness", ResolveBaselineHarnessPath(),
                "--feature-harness", ResolveFeatureHarnessPath(),
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

        public byte[] CreateBenchmarkResultMutation(AnalyzerRun run, double value)
        {
            JsonObject root = LoadObject(_runs[run].Results);
            foreach (JsonNode? item in root["Benchmarks"]!.AsArray())
                item!["Statistics"]!["OriginalValues"] = Samples(value);
            return Encoding.UTF8.GetBytes(
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        public void MutateHarnessProvenance(AnalyzerRun run, Action<JsonObject> mutate)
        {
            JsonObject root = LoadObject(_runs[run].HarnessProvenance);
            mutate(root);
            WriteObject(_runs[run].HarnessProvenance, root);
        }

        public void ReplaceHarnessAssembly(AnalyzerRun run, byte[] bytes, bool updateProvenance)
        {
            File.WriteAllBytes(_runs[run].HarnessAssembly, bytes);
            if (updateProvenance)
            {
                MutateHarnessProvenance(
                    run,
                    root => root["harnessAssemblySha256"] = Sha256(bytes));
            }
        }

        public void MutateHarnessBuildInput(AnalyzerRun run)
        {
            MutateHarnessProvenance(run, root =>
            {
                JsonObject inputs = root["buildInputSha256"]!.AsObject();
                string first = inputs.Select(static item => item.Key).Order(StringComparer.Ordinal).First();
                inputs[first] = AlternateSha256;
                var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach ((string path, JsonNode? value) in inputs)
                    hashes.Add(path, value!.GetValue<string>());
                root["buildInputBundleSha256"] =
                    BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(hashes);
            });
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

        public string GetOutputBlobPath(AnalyzerRun run, string file)
            => Path.Combine(_runs[run].OutputBlobs, file);

        public void IntroduceLocalizedDefect(OutputPhase phase, LocalizedDefect defect)
        {
            const string caseName = "ParameterOnlyAnimation";
            RenderPipelineBenchmarkSceneDefinition scene = RenderPipelineBenchmarkScenes.All
                .Single(static item => item.Name == caseName);
            byte[] reference = CreateCheckerboardBlob(scene);
            foreach (AnalyzerRun run in Enum.GetValues<AnalyzerRun>())
            {
                foreach (OutputPhase outputPhase in Enum.GetValues<OutputPhase>())
                {
                    ReplaceOutputBlob(
                        run,
                        caseName,
                        outputPhase,
                        reference.ToArray(),
                        updateCounterContract: true);
                }
            }

            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            byte[] corrupted = reference.ToArray();
            switch (defect)
            {
                case LocalizedDefect.Rgb:
                    for (int y = 0; y < 14; y++)
                    {
                        for (int x = 0; x < 14; x++)
                            WritePixel(corrupted, size.Width, x, y, 0.5f, 0.5f, 0.5f, 1);
                    }
                    break;
                case LocalizedDefect.Alpha:
                    for (int y = 0; y < 14; y++)
                    {
                        for (int x = 0; x < 14; x++)
                        {
                            float value = (x + y) % 2 == 0 ? 1 : 0;
                            WritePixel(corrupted, size.Width, x, y, value, value, value, 0);
                        }
                    }
                    break;
                case LocalizedDefect.GridBoundary:
                    for (int y = 12; y < 20; y++)
                    {
                        for (int x = 12; x < 20; x++)
                        {
                            float value = (x + y) % 2 == 0 ? 0 : 1;
                            WritePixel(corrupted, size.Width, x, y, value, value, value, 1);
                        }
                    }
                    break;
                case LocalizedDefect.SparseGridBoundary:
                    for (int y = 12; y < 20; y++)
                    {
                        for (int x = 12; x < 20; x++)
                        {
                            if (x % 2 == 0 && y % 2 == 0)
                                WritePixel(corrupted, size.Width, x, y, 0, 0, 0, 1);
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(defect), defect, null);
            }

            ReplaceOutputBlob(
                AnalyzerRun.Feature,
                caseName,
                phase,
                corrupted,
                updateCounterContract: true);
        }

        public void IntroduceTailAnchoredAlphaDefect(OutputPhase phase)
        {
            const string caseName = "SmallObjectFixedOverhead";
            RenderPipelineBenchmarkSceneDefinition scene = RenderPipelineBenchmarkScenes.All
                .Single(static item => item.Name == caseName);
            byte[] reference = CreateSyntheticBlob(scene);
            foreach (AnalyzerRun run in Enum.GetValues<AnalyzerRun>())
            {
                foreach (OutputPhase outputPhase in Enum.GetValues<OutputPhase>())
                {
                    ReplaceOutputBlob(
                        run,
                        caseName,
                        outputPhase,
                        reference.ToArray(),
                        updateCounterContract: true);
                }
            }

            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            byte[] corrupted = reference.ToArray();
            foreach ((int x, int y) in new[]
                     {
                         (27, 6),
                         (28, 12),
                         (29, 13),
                         (30, 14),
                         (31, 15),
                         (32, 15),
                         (31, 16),
                     })
            {
                WritePixel(corrupted, size.Width, x, y, 0.5f, 0.5f, 0.5f, 0);
            }

            foreach (OutputPhase outputPhase in Enum.GetValues<OutputPhase>())
            {
                ReplaceOutputBlob(
                    AnalyzerRun.Feature,
                    caseName,
                    outputPhase,
                    corrupted.ToArray(),
                    updateCounterContract: true);
            }

            OutputPhase matchingPhase = phase == OutputPhase.Setup
                ? OutputPhase.Measured
                : OutputPhase.Setup;
            foreach (AnalyzerRun run in new[] { AnalyzerRun.Baseline, AnalyzerRun.BaselineRepeat })
            {
                ReplaceOutputBlob(
                    run,
                    caseName,
                    matchingPhase,
                    corrupted.ToArray(),
                    updateCounterContract: true);
            }
        }

        public void IntroduceStaticTailPixelDefect()
        {
            const string caseName = "SmallObjectFixedOverhead";
            RenderPipelineBenchmarkSceneDefinition scene = RenderPipelineBenchmarkScenes.All
                .Single(static item => item.Name == caseName);
            byte[] corrupted = CreateSyntheticBlob(scene);
            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            WritePixel(corrupted, size.Width, 37, 21, 0.5f, 0.5f, 0.5f, 0);
            foreach (OutputPhase phase in Enum.GetValues<OutputPhase>())
            {
                ReplaceOutputBlob(
                    AnalyzerRun.Feature,
                    caseName,
                    phase,
                    corrupted.ToArray(),
                    updateCounterContract: true);
            }
        }

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

        private PairedBenchmarkAnalyzerOptions CreateOptions(
            string featureSha = FeatureSha,
            string harnessRepositoryHead = FeatureSha,
            Action<string>? resultSnapshotCaptured = null,
            Action<string>? provenanceSnapshotCaptured = null,
            Action<string>? assemblySnapshotCaptured = null,
            bool useSyntheticAssemblyMetadata = true,
            Func<string, byte[], HarnessAssemblyMetadata>? assemblyMetadataReader = null,
            string? featureResultsPath = null,
            string? featureHarnessProvenancePath = null,
            string? featureHarnessAssemblyPath = null)
        {
            RunPaths baseline = _runs[AnalyzerRun.Baseline];
            RunPaths repeat = _runs[AnalyzerRun.BaselineRepeat];
            RunPaths feature = _runs[AnalyzerRun.Feature];
            return new PairedBenchmarkAnalyzerOptions
            {
                BaselineResultsPath = baseline.Results,
                BaselineRepeatResultsPath = repeat.Results,
                FeatureResultsPath = featureResultsPath ?? feature.Results,
                BaselineCountersPath = baseline.Counters,
                BaselineRepeatCountersPath = repeat.Counters,
                FeatureCountersPath = feature.Counters,
                BaselineStdoutPath = baseline.Stdout,
                BaselineRepeatStdoutPath = repeat.Stdout,
                FeatureStdoutPath = feature.Stdout,
                BaselineHarnessProvenancePath = baseline.HarnessProvenance,
                BaselineRepeatHarnessProvenancePath = repeat.HarnessProvenance,
                FeatureHarnessProvenancePath = featureHarnessProvenancePath ?? feature.HarnessProvenance,
                BaselineHarnessAssemblyPath = baseline.HarnessAssembly,
                BaselineRepeatHarnessAssemblyPath = repeat.HarnessAssembly,
                FeatureHarnessAssemblyPath = featureHarnessAssemblyPath ?? feature.HarnessAssembly,
                BaselineOutputsPath = baseline.OutputBlobs,
                BaselineRepeatOutputsPath = repeat.OutputBlobs,
                FeatureOutputsPath = feature.OutputBlobs,
                BaselineSha = BaselineSha,
                FeatureSha = featureSha,
                BaselineCommand = "synthetic baseline A",
                BaselineRepeatCommand = "synthetic baseline B",
                FeatureCommand = "synthetic feature",
                RunnerSha256 = RunnerSha256,
                BaselineHarnessPath = ResolveBaselineHarnessPath(),
                FeatureHarnessPath = ResolveFeatureHarnessPath(),
                OutputPath = OutputPath,
                BootstrapIterations = 1000,
                BenchmarkResultSnapshotCaptured = resultSnapshotCaptured,
                HarnessProvenanceSnapshotCaptured = provenanceSnapshotCaptured,
                HarnessAssemblySnapshotCaptured = assemblySnapshotCaptured,
                HarnessAssemblyMetadataReader = useSyntheticAssemblyMetadata
                    ? assemblyMetadataReader ?? CreateSyntheticHarnessAssemblyMetadata
                    : null,
                HarnessBuildInputVerifier = (_, expectedHead, _, label) =>
                {
                    if (!string.Equals(harnessRepositoryHead, expectedHead, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"{label} harness repository HEAD {harnessRepositoryHead} does not match "
                            + $"feature revision {expectedHead}.");
                    }
                },
            };
        }

        private static HarnessAssemblyMetadata CreateSyntheticHarnessAssemblyMetadata(
            string path,
            byte[] bytes)
        {
            bool baseline = Path.GetFileName(path).StartsWith(
                "Beutl.GpuPassTargetBenchmarkHarness",
                StringComparison.Ordinal);
            SortedDictionary<string, string> buildInputs = baseline
                ? new SortedDictionary<string, string>(s_baselineHarnessBuildInputs.Value, StringComparer.Ordinal)
                : new SortedDictionary<string, string>(s_featureHarnessBuildInputs.Value, StringComparer.Ordinal);
            return new HarnessAssemblyMetadata(
                baseline ? "Beutl.GpuPassTargetBenchmarkHarness" : "Beutl.Benchmarks",
                $"2.99.99+{FeatureSha}",
                FeatureSha,
                BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(buildInputs),
                buildInputs);
        }

        private RunPaths CreateRun(
            string name,
            string sourceSha,
            double sampleValue,
            string harnessAssemblyName,
            SortedDictionary<string, string> harnessBuildInputs,
            byte[] harnessAssemblyBytes)
        {
            string directory = Path.Combine(Root, name);
            string counters = Path.Combine(directory, "counters");
            Directory.CreateDirectory(counters);
            string results = Path.Combine(directory, "results.json");
            string stdout = Path.Combine(directory, "stdout.txt");
            string harnessProvenance = Path.Combine(directory, "harness-provenance.json");
            string harnessAssembly = Path.Combine(directory, harnessAssemblyName + ".dll");
            string outputBlobs = Path.Combine(directory, "output-blobs");
            Directory.CreateDirectory(outputBlobs);
            WriteBenchmarkResults(results, sampleValue);
            File.WriteAllText(stdout, "synthetic benchmark output\n", new UTF8Encoding(false));
            using (var stream = new FileStream(
                       harnessAssembly,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(harnessAssemblyBytes);
            }
            WriteHarnessProvenance(
                harnessProvenance,
                harnessAssemblyName,
                Sha256(harnessAssemblyBytes),
                harnessBuildInputs);
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

            return new RunPaths(
                results,
                counters,
                stdout,
                outputBlobs,
                harnessProvenance,
                harnessAssembly);
        }

        private static void WriteHarnessProvenance(
            string path,
            string harnessAssemblyName,
            string harnessAssemblySha256,
            SortedDictionary<string, string> buildInputSha256)
        {
            WriteObject(path, new JsonObject
            {
                ["schemaVersion"] = 2,
                ["harnessAssemblyName"] = harnessAssemblyName,
                ["harnessAssemblyVersion"] = $"2.99.99+{FeatureSha}",
                ["sourceRevision"] = FeatureSha,
                ["harnessAssemblySha256"] = harnessAssemblySha256,
                ["buildInputBundleSha256"] =
                    BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(buildInputSha256),
                ["buildInputSha256"] = new JsonObject(
                    buildInputSha256.Select(static item =>
                        KeyValuePair.Create<string, JsonNode?>(item.Key, JsonValue.Create(item.Value)))),
            });
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

        private static byte[] CreateCheckerboardBlob(RenderPipelineBenchmarkSceneDefinition scene)
        {
            PixelSize size = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            var payload = new byte[checked(size.Width * size.Height * 8)];
            for (int y = 0; y < size.Height; y++)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    float value = (x + y) % 2 == 0 ? 1 : 0;
                    WritePixel(payload, size.Width, x, y, value, value, value, 1);
                }
            }
            return payload;
        }

        private static void WritePixel(
            Span<byte> payload,
            int width,
            int x,
            int y,
            float red,
            float green,
            float blue,
            float alpha)
        {
            int offset = checked((y * width + x) * 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                payload.Slice(offset, 2),
                BitConverter.HalfToUInt16Bits((Half)red));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                payload.Slice(offset + 2, 2),
                BitConverter.HalfToUInt16Bits((Half)green));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                payload.Slice(offset + 4, 2),
                BitConverter.HalfToUInt16Bits((Half)blue));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                payload.Slice(offset + 6, 2),
                BitConverter.HalfToUInt16Bits((Half)alpha));
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

        private static string ResolveFeatureHarnessPath()
            => Path.Combine(
                GpuPassFusionEvidencePaths.Discover().RepositoryRoot,
                "tests",
                "Beutl.Benchmarks");

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

        internal sealed record RunPaths(
            string Results,
            string Counters,
            string Stdout,
            string OutputBlobs,
            string HarnessProvenance,
            string HarnessAssembly);
    }
}

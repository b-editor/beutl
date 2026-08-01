using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Benchmarks.Rendering;
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

        int exitCode = PairedBenchmarkAnalyzer.Run(fixture.CreateArguments(), output, error);

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
        });
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

    private sealed class AnalyzerFixture : IDisposable
    {
        public const string AlternateSha256 =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        public const string AlternateChecksum = "dddddddddddddddd";

        private const string BaselineSha = "43a38e665d9bf52548161a3917e748bd1457ff55";
        private const string FeatureSha = "1111111111111111111111111111111111111111";
        private const string RunnerSha256 =
            "2222222222222222222222222222222222222222222222222222222222222222";
        private const string OutputSha256 =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string OutputChecksum = "bbbbbbbbbbbbbbbb";

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
                "--baseline-sha", BaselineSha,
                "--feature-sha", FeatureSha,
                "--baseline-command", "synthetic baseline A",
                "--baseline-repeat-command", "synthetic baseline B",
                "--feature-command", "synthetic feature",
                "--runner-sha256", RunnerSha256,
                "--baseline-harness", GpuPassFusionEvidencePaths.Discover().EvidenceDirectory
                    + Path.DirectorySeparatorChar + "target-benchmark-harness",
                "--output", OutputPath,
                "--bootstrap-iterations", "1000",
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

        public void MutateCounter(AnalyzerRun run, string caseName, Action<JsonObject> mutate)
        {
            string path = Path.Combine(_runs[run].Counters, caseName + ".json");
            JsonObject root = LoadObject(path);
            mutate(root);
            WriteObject(path, root);
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
            WriteBenchmarkResults(results, sampleValue);
            File.WriteAllText(stdout, "synthetic benchmark output\n", new UTF8Encoding(false));
            foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
                WriteCounter(Path.Combine(counters, scene.Name + ".json"), scene.Name, sourceSha);
            return new RunPaths(results, counters, stdout);
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

        private static void WriteCounter(string path, string caseName, string sourceSha)
        {
            WriteObject(path, new JsonObject
            {
                ["schemaVersion"] = 2,
                ["caseName"] = caseName,
                ["seed"] = 20_040_719,
                ["width"] = 384,
                ["height"] = 216,
                ["setupWarmupFrames"] = 5,
                ["lifetime"] = "persistent-renderer",
                ["requestShape"] = "complete-target-frame",
                ["outputSha256"] = OutputSha256,
                ["outputChecksum"] = OutputChecksum,
                ["outputBounds"] = Bounds(),
                ["measuredOutputSha256"] = OutputSha256,
                ["measuredOutputChecksum"] = OutputChecksum,
                ["measuredOutputBounds"] = Bounds(),
                ["measuredWidth"] = 384,
                ["measuredHeight"] = 216,
                ["expectedMeasuredOutputSha256"] = OutputSha256,
                ["expectedMeasuredOutputChecksum"] = OutputChecksum,
                ["expectedMeasuredOutputBounds"] = Bounds(),
                ["expectedMeasuredWidth"] = 384,
                ["expectedMeasuredHeight"] = 216,
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

        private static JsonObject Bounds()
            => new()
            {
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 384,
                ["height"] = 216,
            };

        private static JsonObject LoadObject(string path)
            => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();

        private static void WriteObject(string path, JsonObject value)
            => File.WriteAllText(
                path,
                value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));

        private sealed record RunPaths(string Results, string Counters, string Stdout);
    }
}

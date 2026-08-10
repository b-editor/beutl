using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Benchmarks.Rendering;

internal static class PairedBenchmarkAnalyzer
{
    internal const int DefaultBootstrapIterations = 100_000;
    private const int BootstrapSeed = RenderPipelineBenchmarkScenes.SourceSeed;
    private const string PrimaryCaseName = "ShaderOpacityShader";
    private const string ExactOutputControlCaseName = "NoEffectControl";
    private const string SourceProvenanceField = "beutlEngineAssemblyVersion";
    private const string BaselineHarnessAssemblyName = "Beutl.GpuPassTargetBenchmarkHarness";
    private const string FeatureHarnessAssemblyName = "Beutl.Benchmarks";
    private const double MaximumBaselineRepeatSymmetricToleranceFactor = 1.20;
    private const int LocalizedParityWindowSize = 16;
    private const int LocalizedParityWindowOffset = LocalizedParityWindowSize / 2;
    private const double MinimumLocalizedSsim = 0.95;
    private const double MaximumLocalizedAlphaMae = 0.02;
    private const double MaximumLocalizedRgbaMae = 0.05;

    private static readonly HashSet<string> s_controlAndBarrierCases = new(StringComparer.Ordinal)
    {
        "NoEffectControl",
        "ShaderOpacityShaderBarrier",
        "MixedSpatialColor",
        "MultipleDrawablesTargetDependencies",
    };

    private static readonly string[] s_requiredFingerprintFields =
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

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            PairedBenchmarkAnalyzerOptions options = PairedBenchmarkAnalyzerOptions.Parse(args);
            return RunCore(options, output, error);
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static int Run(
        PairedBenchmarkAnalyzerOptions options,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            return RunCore(options, output, error);
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunCore(
        PairedBenchmarkAnalyzerOptions options,
        TextWriter output,
        TextWriter error)
    {
        PairedBenchmarkManifest manifest = Analyze(options);
        string? parent = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        using (var stream = new FileStream(
                   options.OutputPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            JsonSerializer.Serialize(stream, manifest, PairedBenchmarkManifest.JsonOptions);
            stream.WriteByte((byte)'\n');
        }

        output.WriteLine(
            $"Primary {PrimaryCaseName} ratio={manifest.Cases[PrimaryCaseName].MedianRatio:R}, "
            + $"95% CI=[{manifest.Cases[PrimaryCaseName].ConfidenceInterval95.Lower:R}, "
            + $"{manifest.Cases[PrimaryCaseName].ConfidenceInterval95.Upper:R}]");
        output.WriteLine(
            $"Baseline repeat stability={manifest.BaselineRepeatStable}; "
            + $"control/barrier acceptance={manifest.ControlBarrierAcceptancePassed}");
        output.WriteLine($"Manifest: {options.OutputPath}");
        if (!manifest.OverallAcceptancePassed)
        {
            error.WriteLine(
                "Paired benchmark acceptance failed; inspect the separate primary, baseline-repeat, "
                + "and control/barrier gates in the manifest.");
            return 2;
        }
        return 0;
    }

    public static int RunSelfTest(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            double[] baseline = Enumerable.Repeat(100d, 15).ToArray();
            double[] feature = Enumerable.Repeat(80d, 15).ToArray();
            PairedBootstrapResult first = BootstrapMedianRatio(
                baseline,
                feature,
                iterations: 2_000,
                seed: BootstrapSeed);
            PairedBootstrapResult second = BootstrapMedianRatio(
                baseline,
                feature,
                iterations: 2_000,
                seed: BootstrapSeed);
            if (first != second || first.MedianRatio != 0.8
                || first.ConfidenceInterval95 != new PairedConfidenceInterval(0.8, 0.8))
            {
                throw new InvalidOperationException("Deterministic bootstrap self-test failed.");
            }

            PairedBootstrapResult identity = BootstrapMedianRatio(
                baseline,
                baseline,
                iterations: 2_000,
                seed: BootstrapSeed);
            if (identity.MedianRatio != 1
                || identity.ConfidenceInterval95 != new PairedConfidenceInterval(1, 1))
            {
                throw new InvalidOperationException("Identity-ratio bootstrap self-test failed.");
            }

            BaselineRepeatTolerance stableTolerance = DeriveBaselineRepeatTolerance(
                new PairedConfidenceInterval(0.95, 1.04));
            if (!stableTolerance.Stable
                || !stableTolerance.ConfidenceContainsOne
                || Math.Abs(stableTolerance.Factor - (1 / 0.95)) > 1e-12
                || Math.Abs(stableTolerance.Interval.Lower - 0.95) > 1e-12
                || Math.Abs(stableTolerance.Interval.Upper - (1 / 0.95)) > 1e-12)
            {
                throw new InvalidOperationException("Baseline-repeat symmetric-tolerance self-test failed.");
            }
            BaselineRepeatTolerance driftedTolerance = DeriveBaselineRepeatTolerance(
                new PairedConfidenceInterval(1.01, 1.03));
            BaselineRepeatTolerance noisyTolerance = DeriveBaselineRepeatTolerance(
                new PairedConfidenceInterval(0.8, 1.05));
            if (driftedTolerance.Stable || noisyTolerance.Stable)
            {
                throw new InvalidOperationException("Unstable baseline-repeat controls were accepted.");
            }

            var left = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["device"] = "one",
                ["runtime"] = "same",
            };
            var right = new SortedDictionary<string, string>(left, StringComparer.Ordinal)
            {
                ["device"] = "two",
            };
            bool rejected = false;
            try
            {
                AssertMatchingEnvironmentFingerprints(left, right);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            if (!rejected)
                throw new InvalidOperationException("Fingerprint mismatch self-test did not fail hard.");

            bool invalidSamplesRejected = false;
            try
            {
                BootstrapMedianRatio([1, double.NaN], [1, 2], 10, BootstrapSeed);
            }
            catch (ArgumentException)
            {
                invalidSamplesRejected = true;
            }
            if (!invalidSamplesRejected)
                throw new InvalidOperationException("Invalid samples were accepted by the bootstrap implementation.");

            var exactOutput = new CounterOutputContract(
                384,
                216,
                "{\"height\":216,\"width\":384,\"x\":0,\"y\":0}",
                new string('a', 64),
                new string('b', 16));
            CounterOutputContract[] mismatchedOutputs =
            [
                exactOutput with { Width = 385 },
                exactOutput with { Height = 217 },
                exactOutput with { Bounds = "{\"height\":216,\"width\":384,\"x\":1,\"y\":0}" },
                exactOutput with { Sha256 = new string('c', 64) },
                exactOutput with { Checksum = new string('d', 16) },
            ];
            foreach (CounterOutputContract mismatch in mismatchedOutputs)
            {
                bool outputMismatchRejected = false;
                try
                {
                    ValidateExactOutputContract(ExactOutputControlCaseName, exactOutput, mismatch);
                }
                catch (InvalidDataException)
                {
                    outputMismatchRejected = true;
                }
                if (!outputMismatchRejected)
                {
                    throw new InvalidOperationException(
                        "An exact NoEffectControl output-contract mismatch was accepted.");
                }
            }
            ValidateExactOutputContract(ExactOutputControlCaseName, exactOutput, exactOutput);

            using JsonDocument stringBounds = JsonDocument.Parse("\"0, 0, 384, 216\"");
            using JsonDocument objectBounds = JsonDocument.Parse(
                "{\"x\":0,\"y\":0,\"width\":384,\"height\":216}");
            using JsonDocument emptyBounds = JsonDocument.Parse("\"\"");
            using JsonDocument nonFiniteBounds = JsonDocument.Parse("\"NaN, NaN, NaN, NaN\"");
            using JsonDocument zeroBounds = JsonDocument.Parse("\"0, 0, 0, 0\"");
            using JsonDocument unrelatedBounds = JsonDocument.Parse("{\"left\":0,\"top\":0,\"right\":384,\"bottom\":216}");
            if (!CounterRun.IsValidOutputBounds(stringBounds.RootElement)
                || !CounterRun.IsValidOutputBounds(objectBounds.RootElement)
                || CounterRun.IsValidOutputBounds(emptyBounds.RootElement)
                || CounterRun.IsValidOutputBounds(nonFiniteBounds.RootElement)
                || CounterRun.IsValidOutputBounds(zeroBounds.RootElement)
                || CounterRun.IsValidOutputBounds(unrelatedBounds.RootElement))
            {
                throw new InvalidOperationException(
                    "The output-bounds counter contract did not accept supported serialized forms.");
            }

            output.WriteLine("Paired benchmark analyzer self-test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static PairedBenchmarkManifest Analyze(PairedBenchmarkAnalyzerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateSha(options.BaselineSha, nameof(options.BaselineSha), 40);
        ValidateSha(options.FeatureSha, nameof(options.FeatureSha), 40);
        ValidateSha(options.RunnerSha256, nameof(options.RunnerSha256), 64);
        ValidateOutputPathIndependence(options);
        if (string.Equals(options.BaselineSha, options.FeatureSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "BaselineSha and FeatureSha must identify distinct revisions for a paired comparison.");
        }

        BenchmarkResultRun baselineResults = ReadBenchmarkResults(options.BaselineResultsPath);
        BenchmarkResultRun baselineRepeatResults = ReadBenchmarkResults(options.BaselineRepeatResultsPath);
        BenchmarkResultRun featureResults = ReadBenchmarkResults(options.FeatureResultsPath);
        HarnessProvenanceSnapshot baselineHarness = ReadHarnessProvenance(
            options.BaselineHarnessProvenancePath);
        HarnessProvenanceSnapshot baselineRepeatHarness = ReadHarnessProvenance(
            options.BaselineRepeatHarnessProvenancePath);
        HarnessProvenanceSnapshot featureHarness = ReadHarnessProvenance(
            options.FeatureHarnessProvenancePath);
        Func<string, byte[], HarnessAssemblyMetadata> harnessAssemblyMetadataReader =
            options.HarnessAssemblyMetadataReader ?? ReadHarnessAssemblyMetadata;
        HarnessAssemblySnapshot baselineHarnessAssembly = ReadHarnessAssemblySnapshot(
            options.BaselineHarnessAssemblyPath,
            harnessAssemblyMetadataReader);
        HarnessAssemblySnapshot baselineRepeatHarnessAssembly = ReadHarnessAssemblySnapshot(
            options.BaselineRepeatHarnessAssemblyPath,
            harnessAssemblyMetadataReader);
        HarnessAssemblySnapshot featureHarnessAssembly = ReadHarnessAssemblySnapshot(
            options.FeatureHarnessAssemblyPath,
            harnessAssemblyMetadataReader);
        options.BenchmarkResultSnapshotCaptured?.Invoke(options.BaselineResultsPath);
        options.BenchmarkResultSnapshotCaptured?.Invoke(options.BaselineRepeatResultsPath);
        options.BenchmarkResultSnapshotCaptured?.Invoke(options.FeatureResultsPath);
        options.HarnessProvenanceSnapshotCaptured?.Invoke(options.BaselineHarnessProvenancePath);
        options.HarnessProvenanceSnapshotCaptured?.Invoke(options.BaselineRepeatHarnessProvenancePath);
        options.HarnessProvenanceSnapshotCaptured?.Invoke(options.FeatureHarnessProvenancePath);
        options.HarnessAssemblySnapshotCaptured?.Invoke(options.BaselineHarnessAssemblyPath);
        options.HarnessAssemblySnapshotCaptured?.Invoke(options.BaselineRepeatHarnessAssemblyPath);
        options.HarnessAssemblySnapshotCaptured?.Invoke(options.FeatureHarnessAssemblyPath);
        Action<string, string, IReadOnlyDictionary<string, string>, string> harnessBuildInputVerifier =
            options.HarnessBuildInputVerifier ?? VerifyHarnessBuildInputs;
        harnessBuildInputVerifier(
            options.BaselineHarnessPath,
            options.FeatureSha,
            baselineHarnessAssembly.Metadata.BuildInputSha256,
            "baseline");
        harnessBuildInputVerifier(
            options.FeatureHarnessPath,
            options.FeatureSha,
            featureHarnessAssembly.Metadata.BuildInputSha256,
            "feature");
        ValidateHarnessProvenance(
            baselineHarness,
            baselineHarnessAssembly,
            BaselineHarnessAssemblyName,
            options.FeatureSha,
            baselineHarnessAssembly.Metadata.BuildInputSha256,
            "baseline A");
        ValidateHarnessProvenance(
            baselineRepeatHarness,
            baselineRepeatHarnessAssembly,
            BaselineHarnessAssemblyName,
            options.FeatureSha,
            baselineRepeatHarnessAssembly.Metadata.BuildInputSha256,
            "baseline B");
        ValidateHarnessProvenance(
            featureHarness,
            featureHarnessAssembly,
            FeatureHarnessAssemblyName,
            options.FeatureSha,
            featureHarnessAssembly.Metadata.BuildInputSha256,
            "feature");
        ValidateMatchingHarnessExecutions(
            baselineHarness,
            baselineHarnessAssembly,
            baselineRepeatHarness,
            baselineRepeatHarnessAssembly);
        CounterRun baselineCounters = CounterRun.Read(options.BaselineCountersPath, "baseline");
        CounterRun baselineRepeatCounters = CounterRun.Read(
            options.BaselineRepeatCountersPath,
            "baseline repeat");
        CounterRun featureCounters = CounterRun.Read(options.FeatureCountersPath, "feature");

        ValidateCaseSet(baselineResults.Samples.Keys, "baseline BenchmarkDotNet results");
        ValidateCaseSet(baselineRepeatResults.Samples.Keys, "baseline-repeat BenchmarkDotNet results");
        ValidateCaseSet(featureResults.Samples.Keys, "feature BenchmarkDotNet results");
        ValidateCaseSet(baselineCounters.Cases.Keys, "baseline counters");
        ValidateCaseSet(baselineRepeatCounters.Cases.Keys, "baseline-repeat counters");
        ValidateCaseSet(featureCounters.Cases.Keys, "feature counters");

        ValidateCompatibleBenchmarkRuns(baselineResults, baselineRepeatResults, "baseline repeat");
        ValidateCompatibleBenchmarkRuns(baselineResults, featureResults, "feature");
        ValidateConfiguredSampleCounts(baselineResults, baselineRepeatResults, featureResults);

        ValidateSourceProvenance(
            baselineCounters.SourceProvenance,
            options.BaselineSha,
            "baseline");
        ValidateSourceProvenance(
            baselineRepeatCounters.SourceProvenance,
            options.BaselineSha,
            "baseline repeat");
        ValidateSourceProvenance(
            featureCounters.SourceProvenance,
            options.FeatureSha,
            "feature");
        AssertMatchingEnvironmentFingerprints(
            baselineCounters.EnvironmentFingerprint,
            baselineRepeatCounters.EnvironmentFingerprint);
        AssertMatchingEnvironmentFingerprints(
            baselineCounters.EnvironmentFingerprint,
            featureCounters.EnvironmentFingerprint);
        ValidateOutputBlobs(baselineCounters, options.BaselineOutputsPath, "baseline");
        ValidateOutputBlobs(
            baselineRepeatCounters,
            options.BaselineRepeatOutputsPath,
            "baseline repeat");
        ValidateOutputBlobs(featureCounters, options.FeatureOutputsPath, "feature");
        ValidatePairedCounterContracts(baselineCounters, baselineRepeatCounters, compareEveryOutput: true);
        ValidatePairedCounterContracts(baselineCounters, featureCounters, compareEveryOutput: false);
        ValidateSelfRecordedOutputContracts(baselineCounters, "baseline A");
        ValidateSelfRecordedOutputContracts(baselineRepeatCounters, "baseline B");
        ValidateSelfRecordedOutputContracts(featureCounters, "feature");
        ValidateFeatureOutputContracts(featureCounters);
        ValidateCrossPipelineVisualParity(
            baselineCounters,
            featureCounters,
            options.BaselineOutputsPath,
            options.FeatureOutputsPath);

        var cases = new SortedDictionary<string, PairedBenchmarkCaseResult>(StringComparer.Ordinal);
        foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
        {
            string name = scene.Name;
            double[] baselineSamples = baselineResults.Samples[name];
            double[] baselineRepeatSamples = baselineRepeatResults.Samples[name];
            double[] baselineReferenceSamples = [.. baselineSamples, .. baselineRepeatSamples];
            double[] featureSamples = featureResults.Samples[name];
            PairedBootstrapResult repeatBootstrap = BootstrapMedianRatio(
                baselineSamples,
                baselineRepeatSamples,
                options.BootstrapIterations,
                StableCaseSeed(name) ^ 0x5f37_59df);
            BaselineRepeatTolerance repeatTolerance = DeriveBaselineRepeatTolerance(
                repeatBootstrap.ConfidenceInterval95);
            PairedBootstrapResult bootstrap = BootstrapMedianRatio(
                baselineReferenceSamples,
                featureSamples,
                options.BootstrapIterations,
                StableCaseSeed(name));
            bool isControlOrBarrier = s_controlAndBarrierCases.Contains(name);
            bool noRegressionWithinTolerance =
                bootstrap.ConfidenceInterval95.Upper <= repeatTolerance.Factor;
            cases.Add(name, new PairedBenchmarkCaseResult
            {
                BaselineSampleCount = baselineReferenceSamples.Length,
                BaselineFirstRunSampleCount = baselineSamples.Length,
                BaselineRepeatSampleCount = baselineRepeatSamples.Length,
                FeatureSampleCount = featureSamples.Length,
                BaselineMedianNanoseconds = Median(baselineReferenceSamples),
                BaselineFirstRunMedianNanoseconds = Median(baselineSamples),
                BaselineRepeatMedianNanoseconds = Median(baselineRepeatSamples),
                BaselineRepeatMedianRatio = repeatBootstrap.MedianRatio,
                BaselineRepeatConfidenceInterval95 = repeatBootstrap.ConfidenceInterval95,
                BaselineRepeatConfidenceContainsOne = repeatTolerance.ConfidenceContainsOne,
                BaselineRepeatSymmetricToleranceFactor = repeatTolerance.Factor,
                BaselineRepeatSymmetricToleranceInterval = repeatTolerance.Interval,
                BaselineRepeatStable = repeatTolerance.Stable,
                FeatureMedianNanoseconds = Median(featureSamples),
                MedianRatio = bootstrap.MedianRatio,
                ConfidenceInterval95 = bootstrap.ConfidenceInterval95,
                ConfidenceIntervalEntirelyBelowOne = bootstrap.ConfidenceInterval95.Upper < 1,
                IsControlOrBarrierGateCase = isControlOrBarrier,
                NoRegressionWithinBaselineRepeatTolerance = noRegressionWithinTolerance,
                BaselineCounters = baselineCounters.Cases[name].Record,
                BaselineRepeatCounters = baselineRepeatCounters.Cases[name].Record,
                FeatureCounters = featureCounters.Cases[name].Record,
            });
        }

        bool baselineRepeatStable = cases.Values.All(static item => item.BaselineRepeatStable);
        bool controlBarrierAcceptancePassed = cases.Values
            .Where(static item => item.IsControlOrBarrierGateCase)
            .All(static item => item.NoRegressionWithinBaselineRepeatTolerance);
        bool primaryAcceptancePassed = cases[PrimaryCaseName].ConfidenceIntervalEntirelyBelowOne;

        var environment = baselineCounters.EnvironmentFingerprint.ToDictionary(
            static pair => pair.Key,
            static pair => JsonSerializer.Deserialize<JsonElement>(pair.Value),
            StringComparer.Ordinal);
        return new PairedBenchmarkManifest
        {
            SchemaVersion = 3,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            BootstrapSeed = BootstrapSeed,
            BootstrapIterations = options.BootstrapIterations,
            ConfidenceLevel = 0.95,
            PrimaryCase = PrimaryCaseName,
            PrimaryAcceptanceRule =
                "bootstrap-95%-ci-for-feature-over-pooled-stable-baseline-a-and-b-median-ratio-entirely-below-1.0",
            PrimaryAcceptancePassed = primaryAcceptancePassed,
            BaselineReferenceComposition = "pooled-baseline-a-and-baseline-b-samples-after-repeat-stability-gate",
            BaselineRepeatToleranceFormula =
                "factor=max(repeat-ci-upper,1/repeat-ci-lower); interval=[1/factor,factor]; no clipping",
            BaselineRepeatStabilityRule =
                "repeat-95%-ci-must-contain-1.0-and-derived-symmetric-factor-must-be-at-most-1.20",
            MaximumBaselineRepeatSymmetricToleranceFactor = MaximumBaselineRepeatSymmetricToleranceFactor,
            BaselineRepeatStable = baselineRepeatStable,
            ControlBarrierCases = s_controlAndBarrierCases.Order(StringComparer.Ordinal).ToArray(),
            ControlBarrierAcceptanceRule =
                "feature-over-pooled-baseline-95%-ci-upper-at-most-case-specific-unclipped-repeat-tolerance-factor",
            ControlBarrierAcceptancePassed = controlBarrierAcceptancePassed,
            OverallAcceptancePassed = baselineRepeatStable
                                      && primaryAcceptancePassed
                                      && controlBarrierAcceptancePassed,
            Baseline = CreateRunManifest(
                options.BaselineSha,
                options.BaselineCommand,
                baselineResults,
                options.BaselineStdoutPath,
                options.BaselineCountersPath,
                options.BaselineOutputsPath,
                baselineHarness,
                baselineHarnessAssembly,
                baselineCounters),
            BaselineRepeat = CreateRunManifest(
                options.BaselineSha,
                options.BaselineRepeatCommand,
                baselineRepeatResults,
                options.BaselineRepeatStdoutPath,
                options.BaselineRepeatCountersPath,
                options.BaselineRepeatOutputsPath,
                baselineRepeatHarness,
                baselineRepeatHarnessAssembly,
                baselineRepeatCounters),
            Feature = CreateRunManifest(
                options.FeatureSha,
                options.FeatureCommand,
                featureResults,
                options.FeatureStdoutPath,
                options.FeatureCountersPath,
                options.FeatureOutputsPath,
                featureHarness,
                featureHarnessAssembly,
                featureCounters),
            RunnerSha256 = options.RunnerSha256,
            HarnessBuildInputBundleRule =
                "sha256-over-ordinal-path-null-lowercase-file-sha256-newline-records",
            BaselineHarnessBuildInputSha256 = baselineHarnessAssembly.Metadata.BuildInputSha256,
            FeatureHarnessBuildInputSha256 = featureHarnessAssembly.Metadata.BuildInputSha256,
            EnvironmentFingerprint = new SortedDictionary<string, JsonElement>(environment, StringComparer.Ordinal),
            Cases = cases,
        };
    }

    internal static PairedBootstrapResult BootstrapMedianRatio(
        IReadOnlyList<double> baselineSamples,
        IReadOnlyList<double> featureSamples,
        int iterations,
        int seed)
    {
        ValidateSamples(baselineSamples, nameof(baselineSamples));
        ValidateSamples(featureSamples, nameof(featureSamples));
        if (iterations < 1_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "At least 1000 bootstrap iterations are required.");

        var random = new Random(seed);
        var baselineResample = new double[baselineSamples.Count];
        var featureResample = new double[featureSamples.Count];
        var ratios = new double[iterations];
        for (int iteration = 0; iteration < ratios.Length; iteration++)
        {
            FillResample(baselineSamples, baselineResample, random);
            FillResample(featureSamples, featureResample, random);
            double baselineMedian = MedianInPlace(baselineResample);
            double featureMedian = MedianInPlace(featureResample);
            ratios[iteration] = featureMedian / baselineMedian;
        }
        Array.Sort(ratios);

        return new PairedBootstrapResult(
            Median(featureSamples) / Median(baselineSamples),
            new PairedConfidenceInterval(
                Percentile(ratios, 0.025),
                Percentile(ratios, 0.975)));
    }

    internal static BaselineRepeatTolerance DeriveBaselineRepeatTolerance(
        PairedConfidenceInterval confidenceInterval)
    {
        if (!double.IsFinite(confidenceInterval.Lower)
            || !double.IsFinite(confidenceInterval.Upper)
            || confidenceInterval.Lower <= 0
            || confidenceInterval.Upper < confidenceInterval.Lower)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceInterval),
                confidenceInterval,
                "Baseline-repeat confidence bounds must be finite, positive, and ordered.");
        }

        double factor = Math.Max(confidenceInterval.Upper, 1 / confidenceInterval.Lower);
        bool containsOne = confidenceInterval.Lower <= 1 && confidenceInterval.Upper >= 1;
        return new BaselineRepeatTolerance(
            factor,
            new PairedConfidenceInterval(1 / factor, factor),
            containsOne,
            containsOne && factor <= MaximumBaselineRepeatSymmetricToleranceFactor);
    }

    private static PairedBenchmarkRunManifest CreateRunManifest(
        string sha,
        string command,
        BenchmarkResultRun results,
        string stdoutPath,
        string countersPath,
        string outputsPath,
        HarnessProvenanceSnapshot harness,
        HarnessAssemblySnapshot harnessAssembly,
        CounterRun counters)
    {
        return new PairedBenchmarkRunManifest
        {
            CodeSha = sha,
            EngineAssemblyVersion = counters.SourceProvenance,
            Command = command,
            BenchmarkDotNetResultFile = Path.GetFileName(results.Path),
            BenchmarkDotNetResultSha256 = results.Sha256,
            StandardOutputFile = Path.GetFileName(stdoutPath),
            StandardOutputSha256 = Sha256File(stdoutPath),
            CounterDirectory = Path.GetFileName(Path.TrimEndingDirectorySeparator(countersPath)),
            CounterFileSha256 = counters.FileHashes,
            OutputDirectory = Path.GetFileName(Path.TrimEndingDirectorySeparator(outputsPath)),
            OutputBlobFileSha256 = HashDirectory(outputsPath),
            HarnessProvenanceFile = Path.GetFileName(harness.Path),
            HarnessProvenanceSha256 = harness.Sha256,
            HarnessAssemblyName = harness.Record.HarnessAssemblyName,
            HarnessAssemblyVersion = harness.Record.HarnessAssemblyVersion,
            HarnessSourceRevision = harness.Record.SourceRevision,
            HarnessAssemblySha256 = harness.Record.HarnessAssemblySha256,
            ExecutedHarnessAssemblyFile = Path.GetFileName(harnessAssembly.File.Path),
            ExecutedHarnessAssemblySha256 = harnessAssembly.File.Sha256,
            HarnessBuildInputBundleSha256 = harness.Record.BuildInputBundleSha256,
            BenchmarkDotNetArtifactSha256 = HashDirectory(
                Path.GetDirectoryName(results.Path)
                ?? throw new InvalidDataException($"Benchmark result has no parent directory: {results.Path}"),
                new FileHashSnapshot(results.Path, results.Sha256),
                new FileHashSnapshot(harness.Path, harness.Sha256),
                new FileHashSnapshot(harnessAssembly.File.Path, harnessAssembly.File.Sha256)),
        };
    }

    private static HarnessAssemblySnapshot ReadHarnessAssemblySnapshot(
        string path,
        Func<string, byte[], HarnessAssemblyMetadata> metadataReader)
    {
        RejectPathWithReparsePoints(path, "Executed harness assembly snapshot");
        byte[] bytes = File.ReadAllBytes(path);
        var file = new FileSnapshot(path, bytes, Sha256Bytes(bytes));
        HarnessAssemblyMetadata metadata = metadataReader(path, bytes);
        return new HarnessAssemblySnapshot(file, metadata);
    }

    internal static HarnessAssemblyMetadata ReadHarnessAssemblyMetadataForTest(byte[] bytes)
        => ReadHarnessAssemblyMetadata("test harness assembly", bytes);

    private static HarnessAssemblyMetadata ReadHarnessAssemblyMetadata(string path, byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                throw new InvalidDataException($"Executed harness assembly is not a managed PE image: {path}");
            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                throw new InvalidDataException($"Executed harness PE image has no assembly definition: {path}");

            AssemblyDefinition definition = reader.GetAssemblyDefinition();
            string assemblyName = reader.GetString(definition.Name);
            string? informationalVersion = null;
            var buildInputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string attributeType = GetCustomAttributeTypeName(reader, attribute.Constructor);
                if (string.Equals(
                        attributeType,
                        "System.Reflection.AssemblyInformationalVersionAttribute",
                        StringComparison.Ordinal))
                {
                    string value = ReadStringCustomAttributeArguments(reader, attribute, 1, attributeType)[0];
                    if (informationalVersion is not null)
                    {
                        throw new InvalidDataException(
                            $"Executed harness assembly has duplicate informational-version metadata: {path}");
                    }
                    informationalVersion = value;
                }
                else if (string.Equals(
                             attributeType,
                             "System.Reflection.AssemblyMetadataAttribute",
                             StringComparison.Ordinal))
                {
                    string[] values = ReadStringCustomAttributeArguments(reader, attribute, 2, attributeType);
                    if (!string.Equals(
                            values[0],
                            BenchmarkHarnessProvenance.BuildInputMetadataKey,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    KeyValuePair<string, string> input =
                        BenchmarkHarnessProvenance.ParseBuildInputMetadataValue(values[1]);
                    if (!buildInputs.TryAdd(input.Key, input.Value))
                    {
                        throw new InvalidDataException(
                            $"Executed harness assembly has duplicate build-input metadata: {input.Key}");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new InvalidDataException($"Executed harness assembly has no name: {path}");
            if (informationalVersion is null)
                throw new InvalidDataException($"Executed harness assembly has no informational version: {path}");
            if (buildInputs.Count == 0)
                throw new InvalidDataException($"Executed harness assembly has no build-input metadata: {path}");
            string sourceRevision = BenchmarkHarnessProvenance.ExtractSourceRevision(informationalVersion);
            string bundleSha256 = BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(buildInputs);
            return new HarnessAssemblyMetadata(
                assemblyName,
                informationalVersion,
                sourceRevision,
                bundleSha256,
                buildInputs);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException($"Executed harness assembly is not a valid managed PE image: {path}", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Executed harness assembly metadata is invalid: {path}", exception);
        }
    }

    private static string GetCustomAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                (MethodDefinitionHandle)constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference(
                (MemberReferenceHandle)constructor).Parent,
            _ => default,
        };
        return type.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type)),
            HandleKind.TypeReference => GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)type)),
            _ => string.Empty,
        };
    }

    private static string GetTypeName(MetadataReader reader, TypeDefinition definition)
        => JoinTypeName(reader.GetString(definition.Namespace), reader.GetString(definition.Name));

    private static string GetTypeName(MetadataReader reader, TypeReference reference)
        => JoinTypeName(reader.GetString(reference.Namespace), reader.GetString(reference.Name));

    private static string JoinTypeName(string @namespace, string name)
        => string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";

    private static string[] ReadStringCustomAttributeArguments(
        MetadataReader reader,
        CustomAttribute attribute,
        int count,
        string attributeType)
    {
        BlobReader value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
            throw new InvalidDataException($"{attributeType} has an invalid custom-attribute prolog.");
        var result = new string[count];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = value.ReadSerializedString()
                ?? throw new InvalidDataException($"{attributeType} has a null constructor argument.");
        }
        if (value.ReadUInt16() != 0 || value.RemainingBytes != 0)
            throw new InvalidDataException($"{attributeType} has unexpected named arguments.");
        return result;
    }

    private static BenchmarkResultRun ReadBenchmarkResults(string path)
    {
        RejectPathWithReparsePoints(path, "Benchmark result snapshot");
        byte[] bytes = File.ReadAllBytes(path);
        string sha256 = Sha256Bytes(bytes);
        using JsonDocument document = JsonDocument.Parse(bytes);
        string benchmarkDotNetVersion = document.RootElement
            .GetProperty("HostEnvironmentInfo")
            .GetProperty("BenchmarkDotNetVersion")
            .GetString()
            ?? throw new InvalidDataException($"BenchmarkDotNet version is missing: {path}");
        JsonElement benchmarks = document.RootElement.TryGetProperty("Benchmarks", out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException($"BenchmarkDotNet file has no Benchmarks array: {path}");
        var result = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        string? method = null;
        string? jobDisplay = null;
        foreach (JsonElement benchmark in benchmarks.EnumerateArray())
        {
            string caseName = ParseCaseName(benchmark);
            string currentMethod = benchmark.GetProperty("Method").GetString()
                ?? throw new InvalidDataException($"Benchmark method is missing for '{caseName}'.");
            string currentJobDisplay = ParseJobDisplay(benchmark, caseName);
            if (!string.Equals(
                    currentJobDisplay,
                    RenderPipelineBenchmarkConfig.ExpectedJobDisplay,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Benchmark case '{caseName}' must use the frozen RenderPipelineBenchmarkConfig job "
                    + $"'{RenderPipelineBenchmarkConfig.ExpectedJobDisplay}'; observed '{currentJobDisplay}'.");
            }
            method ??= currentMethod;
            jobDisplay ??= currentJobDisplay;
            if (!string.Equals(method, currentMethod, StringComparison.Ordinal)
                || !string.Equals(jobDisplay, currentJobDisplay, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"BenchmarkDotNet file mixes methods or jobs at case '{caseName}'.");
            }
            JsonElement originalValues = benchmark.GetProperty("Statistics").GetProperty("OriginalValues");
            double[] samples = originalValues.EnumerateArray().Select(static item => item.GetDouble()).ToArray();
            ValidateSamples(samples, caseName);
            if (!result.TryAdd(caseName, samples))
                throw new InvalidDataException($"BenchmarkDotNet results contain duplicate case '{caseName}'.");
        }
        return new BenchmarkResultRun(
            path,
            bytes,
            sha256,
            result,
            method ?? throw new InvalidDataException($"BenchmarkDotNet file contains no methods: {path}"),
            jobDisplay ?? throw new InvalidDataException($"BenchmarkDotNet file contains no jobs: {path}"),
            benchmarkDotNetVersion);
    }

    private static void ValidateCompatibleBenchmarkRuns(
        BenchmarkResultRun baseline,
        BenchmarkResultRun candidate,
        string label)
    {
        if (!string.Equals(baseline.Method, candidate.Method, StringComparison.Ordinal)
            || !string.Equals(baseline.JobDisplay, candidate.JobDisplay, StringComparison.Ordinal)
            || !string.Equals(
                baseline.BenchmarkDotNetVersion,
                candidate.BenchmarkDotNetVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} run must use the same benchmark method, BenchmarkDotNet job, "
                + "and BenchmarkDotNet version as baseline A.");
        }
    }

    private static void ValidateConfiguredSampleCounts(
        BenchmarkResultRun baseline,
        BenchmarkResultRun baselineRepeat,
        BenchmarkResultRun feature)
    {
        foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
        {
            int baselineCount = baseline.Samples[scene.Name].Length;
            int featureCount = feature.Samples[scene.Name].Length;
            int baselineRepeatCount = baselineRepeat.Samples[scene.Name].Length;
            if (baselineCount != RenderPipelineBenchmarkConfig.BenchmarkIterationCount
                || featureCount != RenderPipelineBenchmarkConfig.BenchmarkIterationCount
                || baselineRepeatCount != RenderPipelineBenchmarkConfig.BenchmarkIterationCount
                || baselineCount != featureCount
                || baselineCount != baselineRepeatCount)
            {
                throw new InvalidDataException(
                    $"Benchmark case '{scene.Name}' must contain exactly "
                    + $"{RenderPipelineBenchmarkConfig.BenchmarkIterationCount} matching samples in every run; "
                    + $"observed baseline-a={baselineCount}, feature={featureCount}, "
                    + $"baseline-b={baselineRepeatCount}.");
            }
        }
    }

    private static string ParseJobDisplay(JsonElement benchmark, string caseName)
    {
        string display = benchmark.GetProperty("DisplayInfo").GetString()
            ?? throw new InvalidDataException($"Benchmark display information is missing for '{caseName}'.");
        int separator = display.IndexOf(": ", StringComparison.Ordinal);
        int parameters = display.LastIndexOf(" [CaseName=", StringComparison.Ordinal);
        if (separator < 0 || parameters <= separator + 2)
            throw new InvalidDataException($"Benchmark job information is malformed for '{caseName}'.");
        return display[(separator + 2)..parameters];
    }

    private static string ParseCaseName(JsonElement benchmark)
    {
        if (benchmark.TryGetProperty("FullName", out JsonElement fullName)
            && fullName.ValueKind == JsonValueKind.String)
        {
            string text = fullName.GetString()!;
            const string marker = "CaseName: \"";
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                int end = text.IndexOf('"', start);
                if (end > start)
                    return text[start..end];
            }
        }

        if (benchmark.TryGetProperty("Parameters", out JsonElement parameters)
            && parameters.ValueKind == JsonValueKind.String)
        {
            string text = parameters.GetString()!;
            foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                const string prefix = "CaseName=";
                if (part.StartsWith(prefix, StringComparison.Ordinal))
                    return part[prefix.Length..].Trim().Trim('"');
            }
        }
        throw new InvalidDataException("A BenchmarkDotNet result did not identify its CaseName parameter.");
    }

    private static void ValidateCaseSet(IEnumerable<string> actual, string label)
    {
        string[] expected = RenderPipelineBenchmarkScenes.All
            .Select(static scene => scene.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] found = actual.Order(StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(found, StringComparer.Ordinal))
        {
            string[] missing = expected.Except(found, StringComparer.Ordinal).ToArray();
            string[] extra = found.Except(expected, StringComparer.Ordinal).ToArray();
            throw new InvalidDataException(
                $"{label} case set is incomplete; missing=[{string.Join(",", missing)}], "
                + $"extra=[{string.Join(",", extra)}].");
        }
    }

    private static void ValidateSamples(IReadOnlyList<double> samples, string name)
    {
        if (samples.Count < 2 || samples.Any(static value => !double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentException(
                $"Sample set '{name}' must contain at least two finite positive values.",
                name);
        }
    }

    private static void FillResample(IReadOnlyList<double> source, double[] destination, Random random)
    {
        for (int index = 0; index < destination.Length; index++)
            destination[index] = source[random.Next(source.Count)];
    }

    private static double Median(IReadOnlyList<double> values)
    {
        double[] copy = values.ToArray();
        return MedianInPlace(copy);
    }

    private static double MedianInPlace(double[] values)
    {
        Array.Sort(values);
        int middle = values.Length / 2;
        return (values.Length & 1) != 0
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double probability)
    {
        double position = (sortedValues.Count - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];
        double fraction = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }

    private static int StableCaseSeed(string caseName)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char item in caseName)
            {
                hash ^= item;
                hash *= 16777619;
            }
            return (int)(hash ^ BootstrapSeed);
        }
    }

    private static void AssertMatchingEnvironmentFingerprints(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> feature)
    {
        string[] keys = baseline.Keys.Union(feature.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] mismatches = keys
            .Where(key => !baseline.TryGetValue(key, out string? left)
                          || !feature.TryGetValue(key, out string? right)
                          || !string.Equals(left, right, StringComparison.Ordinal))
            .ToArray();
        if (mismatches.Length != 0)
        {
            throw new InvalidOperationException(
                "Benchmark environment fingerprint mismatch before timing analysis: "
                + string.Join(", ", mismatches));
        }
    }

    private static void ValidatePairedCounterContracts(
        CounterRun baseline,
        CounterRun candidate,
        bool compareEveryOutput)
    {
        foreach (string caseName in baseline.Cases.Keys)
        {
            IReadOnlyDictionary<string, string> baselineContract = baseline.Cases[caseName].Contract;
            IReadOnlyDictionary<string, string> candidateContract = candidate.Cases[caseName].Contract;
            string[] mismatches = baselineContract.Keys
                .Union(candidateContract.Keys, StringComparer.Ordinal)
                .Where(key => !baselineContract.TryGetValue(key, out string? left)
                              || !candidateContract.TryGetValue(key, out string? right)
                              || !string.Equals(left, right, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (mismatches.Length != 0)
            {
                throw new InvalidDataException(
                    $"Paired counter contract mismatch for '{caseName}': {string.Join(", ", mismatches)}.");
            }

            ValidateMatchingCrossPipelineGeometry(
                caseName,
                "setup",
                baseline.Cases[caseName].SetupOutputContract,
                candidate.Cases[caseName].SetupOutputContract);
            ValidateMatchingCrossPipelineGeometry(
                caseName,
                "measured",
                baseline.Cases[caseName].MeasuredOutputContract,
                candidate.Cases[caseName].MeasuredOutputContract);

            // Feature output is intentionally not byte-identical to the frozen baseline for
            // effect workloads (FR-019); cross-pipeline equivalence is proven by the paired
            // visual evidence, so byte equality is required only within a pipeline and for
            // the no-effect control case.
            if (compareEveryOutput
                || string.Equals(caseName, ExactOutputControlCaseName, StringComparison.Ordinal))
            {
                ValidateExactOutputContract(
                    caseName,
                    baseline.Cases[caseName].SetupOutputContract,
                    candidate.Cases[caseName].SetupOutputContract);
                ValidateExactOutputContract(
                    caseName + " measured",
                    baseline.Cases[caseName].MeasuredOutputContract,
                    candidate.Cases[caseName].MeasuredOutputContract);
            }
        }
    }

    private static void ValidateCrossPipelineVisualParity(
        CounterRun baseline,
        CounterRun feature,
        string baselineOutputsPath,
        string featureOutputsPath)
    {
        foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
        {
            CounterCase baselineCase = baseline.Cases[scene.Name];
            CounterCase featureCase = feature.Cases[scene.Name];
            ValidateCrossPipelineOutput(
                scene.Name,
                "setup",
                baselineOutputsPath,
                baselineCase.SetupOutputBlobFile,
                baselineCase.SetupOutputContract,
                featureOutputsPath,
                featureCase.SetupOutputBlobFile,
                featureCase.SetupOutputContract);
            ValidateCrossPipelineOutput(
                scene.Name,
                "measured",
                baselineOutputsPath,
                baselineCase.MeasuredOutputBlobFile,
                baselineCase.MeasuredOutputContract,
                featureOutputsPath,
                featureCase.MeasuredOutputBlobFile,
                featureCase.MeasuredOutputContract);
        }
    }

    private static void ValidateOutputBlobs(CounterRun run, string outputsPath, string label)
    {
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
        {
            CounterCase item = run.Cases[scene.Name];
            if (!expectedFiles.Add(item.SetupOutputBlobFile)
                || !expectedFiles.Add(item.MeasuredOutputBlobFile))
            {
                throw new InvalidDataException(
                    $"{label} output blob mapping contains a duplicate file name.");
            }
            _ = ReadAndValidateOutputBlob(
                outputsPath,
                item.SetupOutputBlobFile,
                scene.Name,
                $"{label} setup",
                item.SetupOutputContract);
            _ = ReadAndValidateOutputBlob(
                outputsPath,
                item.MeasuredOutputBlobFile,
                scene.Name,
                $"{label} measured",
                item.MeasuredOutputContract);
        }

        RejectPathWithReparsePoints(outputsPath, $"{label} output blob directory");
        string[] actualFiles = Directory.EnumerateFileSystemEntries(outputsPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        string[] missing = expectedFiles.Except(actualFiles, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] unreferenced = actualFiles.Except(expectedFiles, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0 || unreferenced.Length != 0)
        {
            throw new InvalidDataException(
                $"{label} output blob directory does not exactly match its counter mappings; "
                + $"missing=[{string.Join(",", missing)}], "
                + $"unreferenced=[{string.Join(",", unreferenced)}].");
        }
    }

    private static void ValidateCrossPipelineOutput(
        string caseName,
        string phase,
        string baselineOutputsPath,
        string baselineBlob,
        CounterOutputContract baselineContract,
        string featureOutputsPath,
        string featureBlob,
        CounterOutputContract featureContract)
    {
        ValidateMatchingCrossPipelineGeometry(caseName, phase, baselineContract, featureContract);
        byte[] baselinePayload = ReadAndValidateOutputBlob(
            baselineOutputsPath,
            baselineBlob,
            caseName,
            $"baseline {phase}",
            baselineContract);
        byte[] featurePayload = ReadAndValidateOutputBlob(
            featureOutputsPath,
            featureBlob,
            caseName,
            $"feature {phase}",
            featureContract);
        Rgba16fParityMetrics full = Rgba16fEvidenceWriter.CalculateParity(
            baselinePayload,
            featurePayload,
            baselineContract.Width,
            baselineContract.Height,
            region: null);
        if (full.LinearLightSsim < 0.99
            || full.LinearRgbMae > 0.02
            || full.AlphaMae > 0.02)
        {
            throw new InvalidDataException(
                $"Benchmark case '{caseName}' feature {phase} output is not visually equivalent to its "
                + $"baseline: SSIM={full.LinearLightSsim:F6}, RGB MAE={full.LinearRgbMae:F6}, "
                + $"alpha MAE={full.AlphaMae:F6}.");
        }

        Rgba16fLocalizedParityMetrics localized = CalculateLocalizedParity(
            baselinePayload,
            featurePayload,
            baselineContract.Width,
            baselineContract.Height);
        if (localized.MinimumSsim < MinimumLocalizedSsim
            || localized.MaximumAlphaMae > MaximumLocalizedAlphaMae
            || localized.MaximumRgbaMae > MaximumLocalizedRgbaMae)
        {
            throw new InvalidDataException(
                $"Benchmark case '{caseName}' feature {phase} output failed the localized "
                + $"{LocalizedParityWindowSize}x{LocalizedParityWindowSize} parity gate: "
                + $"minimum SSIM={localized.MinimumSsim:F6}, "
                + $"maximum alpha MAE={localized.MaximumAlphaMae:F6}, "
                + $"maximum RGBA MAE={localized.MaximumRgbaMae:F6}.");
        }
    }

    private static Rgba16fLocalizedParityMetrics CalculateLocalizedParity(
        ReadOnlySpan<byte> baseline,
        ReadOnlySpan<byte> feature,
        int width,
        int height)
    {
        double minimumSsim = 1;
        double maximumAlphaMae = 0;
        double maximumRgbaMae = 0;
        int[] topStarts = GetLocalizedParityWindowStarts(height);
        int[] leftStarts = GetLocalizedParityWindowStarts(width);
        foreach (int top in topStarts)
        {
            int windowHeight = Math.Min(LocalizedParityWindowSize, height - top);
            foreach (int left in leftStarts)
            {
                int windowWidth = Math.Min(LocalizedParityWindowSize, width - left);
                var region = new PixelRect(left, top, windowWidth, windowHeight);
                Rgba16fParityMetrics metrics = Rgba16fEvidenceWriter.CalculateParity(
                    baseline,
                    feature,
                    width,
                    height,
                    region);
                minimumSsim = Math.Min(minimumSsim, metrics.LinearLightSsim);
                maximumAlphaMae = Math.Max(maximumAlphaMae, metrics.AlphaMae);
                maximumRgbaMae = Math.Max(
                    maximumRgbaMae,
                    ((metrics.LinearRgbMae * 3) + metrics.AlphaMae) / 4);
            }
        }

        return new Rgba16fLocalizedParityMetrics(
            minimumSsim,
            maximumAlphaMae,
            maximumRgbaMae);
    }

    private static int[] GetLocalizedParityWindowStarts(int length)
    {
        var starts = new SortedSet<int>();
        if (length < LocalizedParityWindowSize)
        {
            starts.Add(0);
            return [.. starts];
        }

        for (int start = 0;
             start + LocalizedParityWindowSize <= length;
             start += LocalizedParityWindowSize)
        {
            starts.Add(start);
        }
        for (int start = LocalizedParityWindowOffset;
             start + LocalizedParityWindowSize <= length;
             start += LocalizedParityWindowSize)
        {
            starts.Add(start);
        }

        starts.Add(Math.Max(0, length - LocalizedParityWindowSize));
        return [.. starts];
    }

    private static void ValidateMatchingCrossPipelineGeometry(
        string caseName,
        string phase,
        CounterOutputContract baseline,
        CounterOutputContract feature)
    {
        var mismatches = new List<string>(3);
        if (baseline.Width != feature.Width)
            mismatches.Add("width");
        if (baseline.Height != feature.Height)
            mismatches.Add("height");
        if (!string.Equals(baseline.Bounds, feature.Bounds, StringComparison.Ordinal))
            mismatches.Add("outputBounds");
        if (mismatches.Count != 0)
        {
            throw new InvalidDataException(
                $"Cross-pipeline {phase} output geometry mismatch for '{caseName}': "
                + $"{string.Join(", ", mismatches)}.");
        }
    }

    private static byte[] ReadAndValidateOutputBlob(
        string outputsPath,
        string blobFile,
        string caseName,
        string label,
        CounterOutputContract contract)
    {
        string path = Path.Combine(outputsPath, blobFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Benchmark case '{caseName}' {label} output blob is missing.", path);
        byte[] payload = File.ReadAllBytes(path);
        long expectedLength = checked((long)contract.Width * contract.Height * 8);
        if (payload.LongLength != expectedLength)
        {
            throw new InvalidDataException(
                $"Benchmark case '{caseName}' {label} output blob must contain exactly "
                + $"{expectedLength} RGBA16F bytes; observed {payload.LongLength}.");
        }

        string actualSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (!string.Equals(actualSha256, contract.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Benchmark case '{caseName}' {label} output blob SHA-256 does not match its counter contract; "
                + $"expected {contract.Sha256}, observed {actualSha256}.");
        }

        string actualChecksum = CalculateOutputBlobChecksum(payload);
        if (!string.Equals(actualChecksum, contract.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Benchmark case '{caseName}' {label} output blob checksum does not match its counter contract; "
                + $"expected {contract.Checksum}, observed {actualChecksum}.");
        }
        Rgba16fEvidenceWriter.ValidateFiniteComponents(payload);
        return payload;
    }

    private static string CalculateOutputBlobChecksum(ReadOnlySpan<byte> payload)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong result = offset;
        for (int byteOffset = 0; byteOffset < payload.Length; byteOffset += 26)
        {
            result ^= BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(byteOffset, 2));
            result *= prime;
        }
        return result.ToString("x16");
    }

    private static void ValidateFeatureOutputContracts(CounterRun feature)
    {
        foreach (RenderPipelineBenchmarkSceneDefinition scene in RenderPipelineBenchmarkScenes.All)
        {
            CounterCase item = feature.Cases[scene.Name];
            ValidateMatchingOutputGeometry(
                scene.Name,
                item.SetupOutputContract,
                item.MeasuredOutputContract);
            if (scene.Animation == RenderPipelineBenchmarkAnimation.None)
            {
                ValidateExactOutputContract(
                    scene.Name + " feature setup/measured",
                    item.SetupOutputContract,
                    item.MeasuredOutputContract);
            }
        }
    }

    private static void ValidateSelfRecordedOutputContracts(CounterRun run, string label)
    {
        foreach ((string caseName, CounterCase item) in run.Cases)
        {
            ValidateExactOutputContract(
                $"{caseName} {label} measured/expected",
                item.MeasuredOutputContract,
                item.ExpectedMeasuredOutputContract);
        }
    }

    private static void ValidateMatchingOutputGeometry(
        string caseName,
        CounterOutputContract setup,
        CounterOutputContract measured)
    {
        var mismatches = new List<string>(3);
        if (setup.Width != measured.Width)
            mismatches.Add("width");
        if (setup.Height != measured.Height)
            mismatches.Add("height");
        if (!string.Equals(setup.Bounds, measured.Bounds, StringComparison.Ordinal))
            mismatches.Add("outputBounds");
        if (mismatches.Count != 0)
        {
            throw new InvalidDataException(
                $"Feature output geometry mismatch for '{caseName}': {string.Join(", ", mismatches)}.");
        }
    }

    private static void ValidateExactOutputContract(
        string caseName,
        CounterOutputContract baseline,
        CounterOutputContract feature)
    {
        var mismatches = new List<string>(5);
        if (baseline.Width != feature.Width)
            mismatches.Add("width");
        if (baseline.Height != feature.Height)
            mismatches.Add("height");
        if (!string.Equals(baseline.Bounds, feature.Bounds, StringComparison.Ordinal))
            mismatches.Add("outputBounds");
        if (!string.Equals(baseline.Sha256, feature.Sha256, StringComparison.Ordinal))
            mismatches.Add("outputSha256");
        if (!string.Equals(baseline.Checksum, feature.Checksum, StringComparison.Ordinal))
            mismatches.Add("outputChecksum");
        if (mismatches.Count != 0)
        {
            throw new InvalidDataException(
                $"Paired exact output contract mismatch for '{caseName}': {string.Join(", ", mismatches)}.");
        }
    }

    private static HarnessProvenanceSnapshot ReadHarnessProvenance(string path)
    {
        RejectPathWithReparsePoints(path, "Harness provenance snapshot");
        byte[] bytes = File.ReadAllBytes(path);
        string sha256 = Sha256Bytes(bytes);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string[] expectedFields =
        [
            "schemaVersion",
            "harnessAssemblyName",
            "harnessAssemblyVersion",
            "sourceRevision",
            "harnessAssemblySha256",
            "buildInputBundleSha256",
            "buildInputSha256",
        ];
        string[] actualFields = root.EnumerateObject()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedFields.Order(StringComparer.Ordinal).SequenceEqual(actualFields, StringComparer.Ordinal)
            || root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new InvalidDataException(
                $"Benchmark harness provenance schema mismatch; schema 2 build-time attestation is required: {path}");
        }

        var buildInputSha256 = new SortedDictionary<string, string>(StringComparer.Ordinal);
        JsonElement buildInputs = root.GetProperty("buildInputSha256");
        if (buildInputs.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Benchmark harness build-input map is invalid: {path}");
        foreach (JsonProperty property in buildInputs.EnumerateObject())
        {
            string hash = property.Value.GetString()?.ToLowerInvariant()
                ?? throw new InvalidDataException($"Benchmark harness build-input hash is invalid: {path}");
            if (!buildInputSha256.TryAdd(property.Name, hash))
                throw new InvalidDataException($"Benchmark harness build-input map has duplicate paths: {path}");
        }

        var record = new HarnessProvenanceRecord(
            root.GetProperty("harnessAssemblyName").GetString()
            ?? throw new InvalidDataException($"Harness assembly name is missing: {path}"),
            root.GetProperty("harnessAssemblyVersion").GetString()
            ?? throw new InvalidDataException($"Harness assembly version is missing: {path}"),
            root.GetProperty("sourceRevision").GetString()?.ToLowerInvariant()
            ?? throw new InvalidDataException($"Harness source revision is missing: {path}"),
            root.GetProperty("harnessAssemblySha256").GetString()?.ToLowerInvariant()
            ?? throw new InvalidDataException($"Harness assembly SHA-256 is missing: {path}"),
            root.GetProperty("buildInputBundleSha256").GetString()?.ToLowerInvariant()
            ?? throw new InvalidDataException($"Harness build-input bundle SHA-256 is missing: {path}"),
            buildInputSha256);
        ValidateSha(record.SourceRevision, nameof(record.SourceRevision), 40);
        ValidateSha(record.HarnessAssemblySha256, nameof(record.HarnessAssemblySha256), 64);
        ValidateSha(record.BuildInputBundleSha256, nameof(record.BuildInputBundleSha256), 64);
        try
        {
            string actualBundle = BenchmarkHarnessProvenance.CalculateBuildInputBundleSha256(
                record.BuildInputSha256);
            if (!string.Equals(actualBundle, record.BuildInputBundleSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Benchmark harness build-input bundle SHA-256 does not match its map: {path}");
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"Benchmark harness build-input map is invalid: {path}", ex);
        }
        return new HarnessProvenanceSnapshot(path, bytes, sha256, record);
    }

    private static void ValidateHarnessProvenance(
        HarnessProvenanceSnapshot snapshot,
        HarnessAssemblySnapshot executedAssembly,
        string expectedAssemblyName,
        string expectedSourceRevision,
        IReadOnlyDictionary<string, string> expectedBuildInputs,
        string label)
    {
        HarnessProvenanceRecord record = snapshot.Record;
        HarnessAssemblyMetadata metadata = executedAssembly.Metadata;
        if (!string.Equals(record.HarnessAssemblyName, expectedAssemblyName, StringComparison.Ordinal)
            || !string.Equals(metadata.AssemblyName, expectedAssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{label} harness identity mismatch; expected {expectedAssemblyName}, "
                + $"provenance reported {record.HarnessAssemblyName}, captured PE reported {metadata.AssemblyName}.");
        }
        if (!string.Equals(record.SourceRevision, expectedSourceRevision, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.SourceRevision, expectedSourceRevision, StringComparison.OrdinalIgnoreCase)
            || !record.HarnessAssemblyVersion.Contains(expectedSourceRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{label} harness executable does not identify feature revision {expectedSourceRevision}.");
        }
        if (!string.Equals(record.HarnessAssemblyVersion, metadata.InformationalVersion, StringComparison.Ordinal)
            || !string.Equals(record.SourceRevision, metadata.SourceRevision, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                record.BuildInputBundleSha256,
                metadata.BuildInputBundleSha256,
                StringComparison.OrdinalIgnoreCase)
            || !record.BuildInputSha256.SequenceEqual(metadata.BuildInputSha256))
        {
            throw new InvalidDataException(
                $"{label} harness provenance does not match the captured PE assembly metadata.");
        }
        if (!string.Equals(
                record.HarnessAssemblySha256,
                executedAssembly.File.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{label} harness provenance does not authenticate the independently captured "
                + $"executed assembly snapshot; attested {record.HarnessAssemblySha256}, "
                + $"captured {executedAssembly.File.Sha256}.");
        }

        string[] mismatches = expectedBuildInputs.Keys
            .Union(record.BuildInputSha256.Keys, StringComparer.Ordinal)
            .Where(path => !expectedBuildInputs.TryGetValue(path, out string? expected)
                           || !record.BuildInputSha256.TryGetValue(path, out string? actual)
                           || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (mismatches.Length != 0)
        {
            throw new InvalidDataException(
                $"{label} harness build-time inputs do not match the authenticated repository inputs: "
                + string.Join(", ", mismatches));
        }
    }

    private static void ValidateMatchingHarnessExecutions(
        HarnessProvenanceSnapshot baseline,
        HarnessAssemblySnapshot baselineAssembly,
        HarnessProvenanceSnapshot baselineRepeat,
        HarnessAssemblySnapshot baselineRepeatAssembly)
    {
        HarnessProvenanceRecord left = baseline.Record;
        HarnessProvenanceRecord right = baselineRepeat.Record;
        if (!string.Equals(left.HarnessAssemblyName, right.HarnessAssemblyName, StringComparison.Ordinal)
            || !string.Equals(left.HarnessAssemblyVersion, right.HarnessAssemblyVersion, StringComparison.Ordinal)
            || !string.Equals(left.SourceRevision, right.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(left.HarnessAssemblySha256, right.HarnessAssemblySha256, StringComparison.Ordinal)
            || !string.Equals(
                baselineAssembly.File.Sha256,
                baselineRepeatAssembly.File.Sha256,
                StringComparison.Ordinal)
            || !string.Equals(left.BuildInputBundleSha256, right.BuildInputBundleSha256, StringComparison.Ordinal)
            || !left.BuildInputSha256.SequenceEqual(right.BuildInputSha256))
        {
            throw new InvalidDataException(
                "Baseline A and baseline B were not produced by the same authenticated harness executable.");
        }
    }

    private static void ValidateSourceProvenance(string assemblyVersion, string sha, string label)
    {
        if (!assemblyVersion.Contains(sha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{label} engine assembly version '{assemblyVersion}' does not contain code SHA '{sha}'.");
        }
    }

    private static void ValidateSha(string value, string name, int length)
    {
        if (value.Length != length || value.Any(static item => !Uri.IsHexDigit(item)))
            throw new ArgumentException($"{name} must be a {length}-character hexadecimal value.", name);
    }

    private static void ValidateOutputPathIndependence(PairedBenchmarkAnalyzerOptions options)
    {
        string output = CanonicalizePath(options.OutputPath);
        string[] inputFiles =
        [
            options.BaselineResultsPath,
            options.BaselineRepeatResultsPath,
            options.FeatureResultsPath,
            options.BaselineStdoutPath,
            options.BaselineRepeatStdoutPath,
            options.FeatureStdoutPath,
            options.BaselineHarnessProvenancePath,
            options.BaselineRepeatHarnessProvenancePath,
            options.FeatureHarnessProvenancePath,
            options.BaselineHarnessAssemblyPath,
            options.BaselineRepeatHarnessAssemblyPath,
            options.FeatureHarnessAssemblyPath,
        ];
        foreach (string input in inputFiles)
        {
            if (PathsEqual(output, CanonicalizePath(input)))
            {
                throw new InvalidDataException(
                    $"Manifest output path aliases an analyzer input: {options.OutputPath}");
            }
        }

        string[] inputDirectories =
        [
            options.BaselineCountersPath,
            options.BaselineRepeatCountersPath,
            options.FeatureCountersPath,
            options.BaselineOutputsPath,
            options.BaselineRepeatOutputsPath,
            options.FeatureOutputsPath,
            options.BaselineHarnessPath,
            options.FeatureHarnessPath,
            GetRequiredParentDirectory(options.BaselineResultsPath),
            GetRequiredParentDirectory(options.BaselineRepeatResultsPath),
            GetRequiredParentDirectory(options.FeatureResultsPath),
        ];
        foreach (string input in inputDirectories)
        {
            string directory = Path.TrimEndingDirectorySeparator(CanonicalizePath(input));
            if (PathsEqual(output, directory)
                || output.StartsWith(directory + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new InvalidDataException(
                    $"Manifest output path aliases an analyzer input directory: {options.OutputPath}");
            }
        }

        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException($"Manifest output path already exists: {options.OutputPath}");
    }

    private static string GetRequiredParentDirectory(string path)
        => Path.GetDirectoryName(path)
           ?? throw new InvalidDataException($"Analyzer input has no parent directory: {path}");

    private static string CanonicalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException($"Path has no filesystem root: {path}");
        string current = root;
        string relative = fullPath[root.Length..];
        foreach (string segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(current, segment);
            FileSystemInfo? info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : File.Exists(candidate)
                    ? new FileInfo(candidate)
                    : null;
            current = info?.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
        }
        return Path.GetFullPath(current);
    }

    private static void RejectPathWithReparsePoints(string path, string label)
    {
        string fullPath = Path.GetFullPath(path);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"{label} may not be a symbolic link or reparse point: {fullPath}");
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(left, right, PathComparison);

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Sha256File(string path)
        => Sha256Bytes(File.ReadAllBytes(path));

    private static string Sha256Bytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static SortedDictionary<string, string> HashDirectory(
        string directory,
        params FileHashSnapshot[] snapshots)
    {
        RejectPathWithReparsePoints(directory, "Benchmark artifact directory");
        string root = Path.GetFullPath(directory);
        var overrides = snapshots.ToDictionary(
            static snapshot => Path.GetFullPath(snapshot.Path),
            static snapshot => snapshot.Sha256,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in EnumerateArtifactFiles(root))
        {
            if (overrides.ContainsKey(Path.GetFullPath(path)))
                continue;
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            result.Add(relative, Sha256File(path));
        }
        foreach ((string path, string sha256) in overrides)
        {
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison))
                throw new InvalidDataException($"Evidence snapshot is outside its artifact directory: {path}");
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            result.Add(relative, sha256);
        }
        return result;
    }

    private static IEnumerable<string> EnumerateArtifactFiles(string root)
    {
        var directories = new Stack<string>();
        var files = new List<string>();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*")
                         .Order(StringComparer.Ordinal))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Benchmark artifact inventory may not contain a symbolic link or reparse point: {entry}");
                }
                if (Directory.Exists(entry))
                    directories.Push(entry);
                else if (File.Exists(entry))
                    files.Add(entry);
            }
        }
        return files.Order(StringComparer.Ordinal);
    }

    internal static void VerifyHarnessBuildInputsForTest(
        string directory,
        string expectedRepositoryHead,
        IReadOnlyDictionary<string, string> buildInputSha256)
        => VerifyHarnessBuildInputs(directory, expectedRepositoryHead, buildInputSha256, "test");

    private static void VerifyHarnessBuildInputs(
        string directory,
        string expectedRepositoryHead,
        IReadOnlyDictionary<string, string> buildInputSha256,
        string label)
    {
        string root = CanonicalizePath(directory);
        string repositoryRoot = CanonicalizePath(RunGit(root, "rev-parse", "--show-toplevel").Trim());
        if (!root.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException($"Harness is outside its repository: {root}");
        string repositoryHead = RunGit(repositoryRoot, "rev-parse", "HEAD").Trim().ToLowerInvariant();
        if (repositoryHead.Length != 40 || repositoryHead.Any(static item => !Uri.IsHexDigit(item)))
            throw new InvalidDataException($"Harness repository HEAD is invalid: {repositoryHead}");
        if (!string.Equals(repositoryHead, expectedRepositoryHead, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{label} harness repository HEAD {repositoryHead} does not match "
                + $"feature revision {expectedRepositoryHead}.");
        }
        string repositoryStatus = RunGit(
            repositoryRoot,
            "status",
            "--porcelain=v1",
            "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(repositoryStatus))
        {
            throw new InvalidDataException(
                "Benchmark harness build inputs must come from a clean tracked repository; "
                + repositoryStatus.Trim());
        }
        if (buildInputSha256.Count == 0)
            throw new InvalidDataException($"{label} harness embedded build-input map is empty.");

        var mandatoryPaths = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        string[] projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (projects.Length != 1)
            throw new InvalidDataException($"Harness must contain exactly one project; found {projects.Length}.");
        mandatoryPaths.Add(projects[0]);
        string program = Path.Combine(root, "Program.cs");
        if (!File.Exists(program))
            throw new InvalidDataException($"{label} harness is missing its mandatory Program.cs anchor.");
        mandatoryPaths.Add(program);
        AddInheritedBuildInputs(projects[0], repositoryRoot, mandatoryPaths);

        foreach ((string relative, string expectedSha256) in buildInputSha256)
        {
            _ = BenchmarkHarnessProvenance.ParseBuildInputMetadataValue($"{relative}|{expectedSha256}");
            string path = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), repositoryRoot);
            if (!path.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, PathComparison)
                || !File.Exists(path))
            {
                throw new InvalidDataException(
                    $"{label} harness embedded build input is missing or outside its repository: {relative}");
            }
            RejectPathWithReparsePoints(path, "Harness build input");
            ValidateHarnessBuildInputGitState(repositoryRoot, path, relative);
            string actualSha256 = Sha256File(path);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{label} harness embedded build-input hash does not match working bytes: {relative}");
            }
        }

        string[] missingMandatory = mandatoryPaths
            .Select(path => Path.GetRelativePath(repositoryRoot, path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !buildInputSha256.ContainsKey(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingMandatory.Length != 0)
        {
            throw new InvalidDataException(
                $"{label} harness embedded build-input map omits mandatory inputs: "
                + string.Join(", ", missingMandatory));
        }
        RequireEmbeddedHarnessAnchor(buildInputSha256, "BenchmarkHarnessProvenance.cs", label);
        RequireEmbeddedHarnessAnchor(buildInputSha256, "BenchmarkHarnessProvenance.targets", label);
        if (!buildInputSha256.Keys.Any(static path =>
                path.EndsWith(".cs", StringComparison.Ordinal)
                && Path.GetFileName(path).Contains("Benchmark", StringComparison.Ordinal)
                && !string.Equals(
                    Path.GetFileName(path),
                    "BenchmarkHarnessProvenance.cs",
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"{label} harness embedded build-input map omits its benchmark workload sources.");
        }
    }

    private static void RequireEmbeddedHarnessAnchor(
        IReadOnlyDictionary<string, string> buildInputSha256,
        string fileName,
        string label)
    {
        if (!buildInputSha256.Keys.Any(path =>
                string.Equals(Path.GetFileName(path), fileName, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"{label} harness embedded build-input map omits mandatory anchor {fileName}.");
        }
    }

    private static void ValidateHarnessBuildInputGitState(
        string repositoryRoot,
        string path,
        string relative)
    {
        string indexEntry = RunGit(repositoryRoot, "ls-files", "--stage", "-z", "--", relative);
        if (!indexEntry.EndsWith('\0') || indexEntry.AsSpan(0, indexEntry.Length - 1).Contains('\0'))
            throw new InvalidDataException($"Harness build input has no unique index entry: {relative}");
        ReadOnlySpan<char> entry = indexEntry.AsSpan(0, indexEntry.Length - 1);
        int firstSpace = entry.IndexOf(' ');
        int secondSpace = firstSpace < 0 ? -1 : entry[(firstSpace + 1)..].IndexOf(' ') + firstSpace + 1;
        int tab = secondSpace < 0 ? -1 : entry[(secondSpace + 1)..].IndexOf('\t') + secondSpace + 1;
        if (firstSpace <= 0 || secondSpace <= firstSpace + 1 || tab <= secondSpace + 1)
            throw new InvalidDataException($"Harness build input index entry is malformed: {relative}");
        string mode = entry[..firstSpace].ToString();
        string indexSha = entry[(firstSpace + 1)..secondSpace].ToString();
        string stage = entry[(secondSpace + 1)..tab].ToString();
        string indexPath = entry[(tab + 1)..].ToString();
        if (!string.Equals(stage, "0", StringComparison.Ordinal)
            || !string.Equals(indexPath, relative, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Harness build input has an unsupported index stage: {relative}");
        }
        if (string.Equals(mode, "120000", StringComparison.Ordinal))
            throw new InvalidDataException($"Harness build input may not be a symbolic link: {relative}");
        if (!mode.StartsWith("100", StringComparison.Ordinal))
            throw new InvalidDataException($"Harness build input has unsupported Git mode {mode}: {relative}");

        string indexState = RunGit(repositoryRoot, "ls-files", "-v", "-z", "--", relative);
        if (!string.Equals(indexState, $"H {relative}\0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Harness build input has assume-unchanged, skip-worktree, or another special index state: {relative}");
        }

        string headSha = RunGit(repositoryRoot, "rev-parse", $"HEAD:{relative}").Trim();
        if (!string.Equals(indexSha, headSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Harness build input index differs from repository HEAD: {relative}");
        string workingSha = RunGit(repositoryRoot, "hash-object", $"--path={relative}", "--", path).Trim();
        if (!string.Equals(workingSha, headSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Harness build input working bytes differ from repository HEAD: {relative}");
    }

    private static void AddInheritedBuildInputs(
        string projectPath,
        string repositoryRoot,
        ISet<string> paths)
    {
        string? directory = Path.GetDirectoryName(projectPath);
        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "NuGet.Config",
            "nuget.config",
        ];
        while (directory is not null
               && (PathsEqual(directory, repositoryRoot)
                   || directory.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, PathComparison)))
        {
            foreach (string name in names)
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path))
                    AddRepositoryBuildInput(path, repositoryRoot, paths, name);
            }
            if (PathsEqual(directory, repositoryRoot))
                break;
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void AddRepositoryBuildInput(
        string path,
        string repositoryRoot,
        ISet<string> paths,
        string declaredPath)
    {
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, PathComparison)
            || !File.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"Harness build input is missing or outside the repository: {declaredPath}");
        }
        paths.Add(fullPath);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
        => RunProcess("git", workingDirectory, arguments);

    private static string RunProcess(
        string fileName,
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName} to inspect harness inputs.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not inspect harness inputs with {fileName}: "
                + $"{standardError.Trim()} {standardOutput.Trim()}".Trim());
        }
        return standardOutput;
    }

    private sealed class CounterRun
    {
        private CounterRun(
            SortedDictionary<string, CounterCase> cases,
            SortedDictionary<string, string> environmentFingerprint,
            string sourceProvenance,
            SortedDictionary<string, string> fileHashes)
        {
            Cases = cases;
            EnvironmentFingerprint = environmentFingerprint;
            SourceProvenance = sourceProvenance;
            FileHashes = fileHashes;
        }

        public SortedDictionary<string, CounterCase> Cases { get; }
        public SortedDictionary<string, string> EnvironmentFingerprint { get; }
        public string SourceProvenance { get; }
        public SortedDictionary<string, string> FileHashes { get; }

        public static CounterRun Read(string directory, string label)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"{label} counter directory does not exist: {directory}");

            var cases = new SortedDictionary<string, CounterCase>(StringComparer.Ordinal);
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            SortedDictionary<string, string>? environment = null;
            string? sourceProvenance = null;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.Ordinal))
            {
                byte[] bytes = File.ReadAllBytes(path);
                using JsonDocument document = JsonDocument.Parse(bytes);
                JsonElement root = document.RootElement;
                string caseName = root.GetProperty("caseName").GetString()
                    ?? throw new InvalidDataException($"Counter file has no caseName: {path}");
                int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
                if (schemaVersion != 3)
                    throw new InvalidDataException($"Counter file '{path}' has unsupported schema {schemaVersion}.");
                JsonElement fingerprint = root.GetProperty("fingerprint");
                FingerprintParts parts = ParseFingerprint(fingerprint, path);
                if (environment is null)
                {
                    environment = parts.Environment;
                    sourceProvenance = parts.SourceProvenance;
                }
                else
                {
                    AssertMatchingEnvironmentFingerprints(environment, parts.Environment);
                    if (!string.Equals(sourceProvenance, parts.SourceProvenance, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"{label} counter files disagree on engine source provenance.");
                    }
                }

                CounterCase counterCase = ParseCounterCase(root, path);
                if (!cases.TryAdd(caseName, counterCase))
                    throw new InvalidDataException($"Duplicate {label} counter case '{caseName}'.");
                hashes.Add(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }

            if (environment is null || sourceProvenance is null)
                throw new InvalidDataException($"{label} counter directory contains no JSON records.");
            return new CounterRun(cases, environment, sourceProvenance, hashes);
        }

        private static FingerprintParts ParseFingerprint(JsonElement fingerprint, string path)
        {
            if (fingerprint.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Counter fingerprint is not an object: {path}");
            string[] actual = fingerprint.EnumerateObject()
                .Select(static item => item.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] expected = s_requiredFingerprintFields.Order(StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
                string[] extra = actual.Except(expected, StringComparer.Ordinal).ToArray();
                throw new InvalidDataException(
                    $"Counter fingerprint schema mismatch in '{path}'; "
                    + $"missing=[{string.Join(",", missing)}], extra=[{string.Join(",", extra)}].");
            }

            var environment = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string? provenance = null;
            string? deviceType = fingerprint.EnumerateObject()
                .Where(static item => EvidenceFingerprintRules.IsDeviceTypeField(item.Name)
                                      && item.Value.ValueKind == JsonValueKind.String)
                .Select(static item => item.Value.GetString())
                .FirstOrDefault();
            foreach (JsonProperty property in fingerprint.EnumerateObject())
            {
                ValidateFingerprintValue(property, path, deviceType);
                string canonical = CanonicalJson(property.Value);
                if (property.NameEquals(SourceProvenanceField))
                    provenance = property.Value.GetString();
                else
                    environment.Add(property.Name, canonical);
            }
            return new FingerprintParts(
                environment,
                provenance ?? throw new InvalidDataException($"Fingerprint provenance is missing: {path}"));
        }

        private static CounterCase ParseCounterCase(JsonElement root, string path)
        {
            string caseName = root.GetProperty("caseName").GetString()
                ?? throw new InvalidDataException($"Counter file has no caseName: {path}");
            var contract = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in new[]
                     {
                         "seed", "width", "height", "setupWarmupFrames", "lifetime", "requestShape",
                         "semanticStageCount", "topLevelDrawableCount", "animation", "barrier",
                         "hasStaticPrefixCache", "hasTargetDependencies",
                     })
            {
                if (!root.TryGetProperty(name, out JsonElement value))
                    throw new InvalidDataException($"Counter contract field '{name}' is missing: {path}");
                contract.Add(name, CanonicalJson(value));
            }
            ValidateFrozenWorkloadContract(root, caseName, path);

            string outputSha256 = ReadHexOutput(root, "outputSha256", 64, path);
            string outputChecksum = ReadHexOutput(root, "outputChecksum", 16, path);
            if (!root.TryGetProperty("outputBounds", out JsonElement outputBounds)
                || !TryParseOutputBounds(outputBounds, out Rect parsedOutputBounds))
            {
                throw new InvalidDataException($"Counter output field 'outputBounds' is invalid: {path}");
            }
            string measuredOutputSha256 = ReadHexOutput(root, "measuredOutputSha256", 64, path);
            string measuredOutputChecksum = ReadHexOutput(root, "measuredOutputChecksum", 16, path);
            if (!root.TryGetProperty("measuredOutputBounds", out JsonElement measuredOutputBounds)
                || !TryParseOutputBounds(measuredOutputBounds, out Rect parsedMeasuredOutputBounds))
            {
                throw new InvalidDataException(
                    $"Counter output field 'measuredOutputBounds' is invalid: {path}");
            }
            string expectedMeasuredOutputSha256 = ReadHexOutput(
                root,
                "expectedMeasuredOutputSha256",
                64,
                path);
            string expectedMeasuredOutputChecksum = ReadHexOutput(
                root,
                "expectedMeasuredOutputChecksum",
                16,
                path);
            if (!root.TryGetProperty("expectedMeasuredOutputBounds", out JsonElement expectedMeasuredOutputBounds)
                || !TryParseOutputBounds(
                    expectedMeasuredOutputBounds,
                    out Rect parsedExpectedMeasuredOutputBounds))
            {
                throw new InvalidDataException(
                    $"Counter output field 'expectedMeasuredOutputBounds' is invalid: {path}");
            }

            foreach (string name in new[] { "setupLastRequestCounters", "measuredLastRequestCounters" })
            {
                JsonElement counters = root.GetProperty(name);
                if (counters.ValueKind != JsonValueKind.Object || !counters.EnumerateObject().Any())
                    throw new InvalidDataException($"Counter snapshot '{name}' is empty: {path}");
            }

            int width = root.GetProperty("width").GetInt32();
            int height = root.GetProperty("height").GetInt32();
            int measuredWidth = root.GetProperty("measuredWidth").GetInt32();
            int measuredHeight = root.GetProperty("measuredHeight").GetInt32();
            int expectedMeasuredWidth = root.GetProperty("expectedMeasuredWidth").GetInt32();
            int expectedMeasuredHeight = root.GetProperty("expectedMeasuredHeight").GetInt32();
            if (width <= 0
                || height <= 0
                || measuredWidth <= 0
                || measuredHeight <= 0
                || expectedMeasuredWidth <= 0
                || expectedMeasuredHeight <= 0
                || root.GetProperty("setupWarmupFrames").GetInt32() <= 0)
            {
                throw new InvalidDataException($"Counter dimensions or warm-up count are invalid: {path}");
            }
            if (parsedOutputBounds.Width != width
                || parsedOutputBounds.Height != height
                || parsedMeasuredOutputBounds.Width != measuredWidth
                || parsedMeasuredOutputBounds.Height != measuredHeight
                || parsedExpectedMeasuredOutputBounds.Width != expectedMeasuredWidth
                || parsedExpectedMeasuredOutputBounds.Height != expectedMeasuredHeight)
            {
                throw new InvalidDataException(
                    $"Counter output bounds do not match their bitmap dimensions: {path}");
            }
            return new CounterCase(
                root.Clone(),
                contract,
                new CounterOutputContract(
                    width,
                    height,
                    CanonicalJson(outputBounds),
                    outputSha256,
                    outputChecksum),
                new CounterOutputContract(
                    measuredWidth,
                    measuredHeight,
                    CanonicalJson(measuredOutputBounds),
                    measuredOutputSha256,
                    measuredOutputChecksum),
                new CounterOutputContract(
                    expectedMeasuredWidth,
                    expectedMeasuredHeight,
                    CanonicalJson(expectedMeasuredOutputBounds),
                    expectedMeasuredOutputSha256,
                    expectedMeasuredOutputChecksum),
                ReadOutputBlobFile(
                    root,
                    "setupOutputBlobFile",
                    path,
                    $"{caseName}.setup.rgba16f"),
                ReadOutputBlobFile(
                    root,
                    "measuredOutputBlobFile",
                    path,
                    $"{caseName}.measured.rgba16f"));
        }

        private static string ReadOutputBlobFile(
            JsonElement root,
            string name,
            string path,
            string expectedFileName)
        {
            if (!root.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException($"Counter output blob field '{name}' is missing: {path}");
            }

            string fileName = value.GetString()!;
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
                || fileName.Contains('/')
                || fileName.Contains('\\')
                || !fileName.EndsWith(".rgba16f", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Counter output blob field '{name}' is not a safe RGBA16F file name: {path}");
            }
            if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Counter output blob field '{name}' must use canonical file name "
                    + $"'{expectedFileName}': {path}");
            }
            return fileName;
        }

        private static void ValidateFrozenWorkloadContract(JsonElement root, string caseName, string path)
        {
            RenderPipelineBenchmarkSceneDefinition? scene = RenderPipelineBenchmarkScenes.All
                .SingleOrDefault(item => string.Equals(item.Name, caseName, StringComparison.Ordinal));
            if (scene is null)
                throw new InvalidDataException($"Counter file has an unknown benchmark case '{caseName}': {path}");

            var mismatches = new List<string>(6);
            if (root.GetProperty("seed").GetInt32() != scene.Seed)
                mismatches.Add("seed");
            PixelSize outputSize = RenderPipelineBenchmarkScenes.GetOutputSize(scene);
            if (root.GetProperty("width").GetInt32() != outputSize.Width)
                mismatches.Add("width");
            if (root.GetProperty("height").GetInt32() != outputSize.Height)
                mismatches.Add("height");
            if (root.GetProperty("setupWarmupFrames").GetInt32()
                != RenderPipelineBenchmarkConfig.SetupWarmupFrameCount)
            {
                mismatches.Add("setupWarmupFrames");
            }
            if (!string.Equals(
                    root.GetProperty("lifetime").GetString(),
                    RenderPipelineBenchmarkConfig.LifetimeContract,
                    StringComparison.Ordinal))
            {
                mismatches.Add("lifetime");
            }
            if (!string.Equals(
                    root.GetProperty("requestShape").GetString(),
                    RenderPipelineBenchmarkConfig.RequestShapeContract,
                    StringComparison.Ordinal))
            {
                mismatches.Add("requestShape");
            }
            if (root.GetProperty("semanticStageCount").GetInt32() != scene.SemanticStageCount)
                mismatches.Add("semanticStageCount");
            if (root.GetProperty("topLevelDrawableCount").GetInt32() != scene.TopLevelDrawableCount)
                mismatches.Add("topLevelDrawableCount");
            if (!string.Equals(
                    root.GetProperty("animation").GetString(),
                    scene.Animation.ToString(),
                    StringComparison.Ordinal))
            {
                mismatches.Add("animation");
            }
            if (!string.Equals(
                    root.GetProperty("barrier").GetString(),
                    scene.Barrier.ToString(),
                    StringComparison.Ordinal))
            {
                mismatches.Add("barrier");
            }
            if (root.GetProperty("hasStaticPrefixCache").GetBoolean() != scene.HasStaticPrefixCache)
                mismatches.Add("hasStaticPrefixCache");
            if (root.GetProperty("hasTargetDependencies").GetBoolean() != scene.HasTargetDependencies)
                mismatches.Add("hasTargetDependencies");

            if (mismatches.Count != 0)
            {
                throw new InvalidDataException(
                    $"Counter workload contract for '{caseName}' does not match the frozen benchmark: "
                    + $"{string.Join(", ", mismatches)} ({path}).");
            }
        }

        internal static bool IsValidOutputBounds(JsonElement outputBounds)
            => TryParseOutputBounds(outputBounds, out _);

        private static bool TryParseOutputBounds(JsonElement outputBounds, out Rect bounds)
        {
            bounds = default;
            if (outputBounds.ValueKind == JsonValueKind.String)
            {
                if (!Rect.TryParse(outputBounds.GetString(), out bounds))
                    return false;
            }
            else if (outputBounds.ValueKind == JsonValueKind.Object)
            {
                var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in outputBounds.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetSingle(out float value)
                        || !float.IsFinite(value)
                        || !values.TryAdd(property.Name, value))
                    {
                        return false;
                    }
                }

                if (values.Count != 4
                    || !values.TryGetValue("x", out float x)
                    || !values.TryGetValue("y", out float y)
                    || !values.TryGetValue("width", out float width)
                    || !values.TryGetValue("height", out float height))
                {
                    return false;
                }

                bounds = new Rect(x, y, width, height);
            }
            else
            {
                return false;
            }

            return float.IsFinite(bounds.X)
                   && float.IsFinite(bounds.Y)
                   && float.IsFinite(bounds.Width)
                   && float.IsFinite(bounds.Height)
                   && bounds.Width > 0
                   && bounds.Height > 0;
        }

        private static string ReadHexOutput(JsonElement root, string name, int length, string path)
        {
            string text = root.GetProperty(name).GetString()
                ?? throw new InvalidDataException($"Counter output field '{name}' is missing: {path}");
            if (text.Length != length || text.Any(static item => !Uri.IsHexDigit(item)))
                throw new InvalidDataException($"Counter output field '{name}' is invalid: {path}");
            return text.ToLowerInvariant();
        }

        private static void ValidateFingerprintValue(JsonProperty property, string path, string? deviceType)
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string? text = property.Value.GetString();
                if (text?.Contains("unknown", StringComparison.OrdinalIgnoreCase) == true
                    || (string.IsNullOrWhiteSpace(text)
                        && !EvidenceFingerprintRules.AllowsBlankValue(property.Name, deviceType)))
                {
                    throw new InvalidDataException(
                        $"Fingerprint field '{property.Name}' is missing or unknown in '{path}'.");
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] items = property.Value.EnumerateArray().ToArray();
                if (items.Length == 0
                    || items.Any(static item => item.ValueKind != JsonValueKind.String
                                                || string.IsNullOrWhiteSpace(item.GetString())
                                                || item.GetString()!.Contains(
                                                    "unknown",
                                                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        $"Fingerprint array '{property.Name}' is empty, invalid, or unknown in '{path}'.");
                }
            }
            else
            {
                throw new InvalidDataException(
                    $"Fingerprint field '{property.Name}' has an unsupported value kind in '{path}'.");
            }
        }

        private static string CanonicalJson(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
                JsonValueKind.Object => "{"
                                        + string.Join(
                                            ",",
                                            element.EnumerateObject()
                                                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                                                .Select(
                                                    static item => JsonSerializer.Serialize(item.Name)
                                                                   + ":"
                                                                   + CanonicalJson(item.Value)))
                                        + "}",
                JsonValueKind.Array => "["
                                       + string.Join(
                                           ",",
                                           element.EnumerateArray().Select(static item => CanonicalJson(item)))
                                       + "]",
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => throw new InvalidDataException(
                    $"Unsupported JSON value kind '{element.ValueKind}' in a benchmark contract."),
            };
        }

        private sealed record FingerprintParts(
            SortedDictionary<string, string> Environment,
            string SourceProvenance);
    }

    private sealed record CounterCase(
        JsonElement Record,
        SortedDictionary<string, string> Contract,
        CounterOutputContract SetupOutputContract,
        CounterOutputContract MeasuredOutputContract,
        CounterOutputContract ExpectedMeasuredOutputContract,
        string SetupOutputBlobFile,
        string MeasuredOutputBlobFile);

    private sealed record CounterOutputContract(
        int Width,
        int Height,
        string Bounds,
        string Sha256,
        string Checksum);

    private sealed record BenchmarkResultRun(
        string Path,
        byte[] Bytes,
        string Sha256,
        SortedDictionary<string, double[]> Samples,
        string Method,
        string JobDisplay,
        string BenchmarkDotNetVersion);

    private sealed record HarnessProvenanceSnapshot(
        string Path,
        byte[] Bytes,
        string Sha256,
        HarnessProvenanceRecord Record);

    private sealed record FileSnapshot(
        string Path,
        byte[] Bytes,
        string Sha256);

    private sealed record HarnessAssemblySnapshot(
        FileSnapshot File,
        HarnessAssemblyMetadata Metadata);

    private sealed record HarnessProvenanceRecord(
        string HarnessAssemblyName,
        string HarnessAssemblyVersion,
        string SourceRevision,
        string HarnessAssemblySha256,
        string BuildInputBundleSha256,
        SortedDictionary<string, string> BuildInputSha256);

    private readonly record struct FileHashSnapshot(string Path, string Sha256);
}

internal sealed record HarnessAssemblyMetadata(
    string AssemblyName,
    string InformationalVersion,
    string SourceRevision,
    string BuildInputBundleSha256,
    SortedDictionary<string, string> BuildInputSha256);

internal sealed class PairedBenchmarkAnalyzerOptions
{
    public required string BaselineResultsPath { get; init; }
    public required string BaselineRepeatResultsPath { get; init; }
    public required string FeatureResultsPath { get; init; }
    public required string BaselineCountersPath { get; init; }
    public required string BaselineRepeatCountersPath { get; init; }
    public required string FeatureCountersPath { get; init; }
    public required string BaselineStdoutPath { get; init; }
    public required string BaselineRepeatStdoutPath { get; init; }
    public required string FeatureStdoutPath { get; init; }
    public required string BaselineHarnessProvenancePath { get; init; }
    public required string BaselineRepeatHarnessProvenancePath { get; init; }
    public required string FeatureHarnessProvenancePath { get; init; }
    public required string BaselineHarnessAssemblyPath { get; init; }
    public required string BaselineRepeatHarnessAssemblyPath { get; init; }
    public required string FeatureHarnessAssemblyPath { get; init; }
    public required string BaselineOutputsPath { get; init; }
    public required string BaselineRepeatOutputsPath { get; init; }
    public required string FeatureOutputsPath { get; init; }
    public required string BaselineSha { get; init; }
    public required string FeatureSha { get; init; }
    public required string BaselineCommand { get; init; }
    public required string BaselineRepeatCommand { get; init; }
    public required string FeatureCommand { get; init; }
    public required string RunnerSha256 { get; init; }
    public required string BaselineHarnessPath { get; init; }
    public required string FeatureHarnessPath { get; init; }
    public required string OutputPath { get; init; }
    public int BootstrapIterations { get; init; }
    internal Action<string>? BenchmarkResultSnapshotCaptured { get; init; }
    internal Action<string>? HarnessProvenanceSnapshotCaptured { get; init; }
    internal Action<string>? HarnessAssemblySnapshotCaptured { get; init; }
    internal Func<string, byte[], HarnessAssemblyMetadata>? HarnessAssemblyMetadataReader { get; init; }
    internal Action<string, string, IReadOnlyDictionary<string, string>, string>? HarnessBuildInputVerifier { get; init; }

    public static PairedBenchmarkAnalyzerOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(Usage);
            if (!values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException($"Duplicate option '{args[index]}'.");
        }

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "--baseline-results", "--baseline-repeat-results", "--feature-results",
            "--baseline-counters", "--baseline-repeat-counters", "--feature-counters",
            "--baseline-stdout", "--baseline-repeat-stdout", "--feature-stdout",
            "--baseline-harness-provenance", "--baseline-repeat-harness-provenance",
            "--feature-harness-provenance",
            "--baseline-harness-assembly", "--baseline-repeat-harness-assembly",
            "--feature-harness-assembly",
            "--baseline-outputs", "--baseline-repeat-outputs", "--feature-outputs",
            "--baseline-sha", "--feature-sha", "--baseline-command", "--baseline-repeat-command",
            "--feature-command", "--runner-sha256", "--output",
            "--baseline-harness", "--feature-harness",
        };
        string[] unknown = values.Keys.Where(key => !known.Contains(key)).ToArray();
        if (unknown.Length != 0)
            throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown)}. {Usage}");

        return new PairedBenchmarkAnalyzerOptions
        {
            BaselineResultsPath = FilePath(Require(values, "--baseline-results")),
            BaselineRepeatResultsPath = FilePath(Require(values, "--baseline-repeat-results")),
            FeatureResultsPath = FilePath(Require(values, "--feature-results")),
            BaselineCountersPath = DirectoryPath(Require(values, "--baseline-counters")),
            BaselineRepeatCountersPath = DirectoryPath(Require(values, "--baseline-repeat-counters")),
            FeatureCountersPath = DirectoryPath(Require(values, "--feature-counters")),
            BaselineStdoutPath = FilePath(Require(values, "--baseline-stdout")),
            BaselineRepeatStdoutPath = FilePath(Require(values, "--baseline-repeat-stdout")),
            FeatureStdoutPath = FilePath(Require(values, "--feature-stdout")),
            BaselineHarnessProvenancePath = FilePath(Require(values, "--baseline-harness-provenance")),
            BaselineRepeatHarnessProvenancePath = FilePath(
                Require(values, "--baseline-repeat-harness-provenance")),
            FeatureHarnessProvenancePath = FilePath(Require(values, "--feature-harness-provenance")),
            BaselineHarnessAssemblyPath = FilePath(Require(values, "--baseline-harness-assembly")),
            BaselineRepeatHarnessAssemblyPath = FilePath(
                Require(values, "--baseline-repeat-harness-assembly")),
            FeatureHarnessAssemblyPath = FilePath(Require(values, "--feature-harness-assembly")),
            BaselineOutputsPath = DirectoryPath(Require(values, "--baseline-outputs")),
            BaselineRepeatOutputsPath = DirectoryPath(Require(values, "--baseline-repeat-outputs")),
            FeatureOutputsPath = DirectoryPath(Require(values, "--feature-outputs")),
            BaselineSha = Require(values, "--baseline-sha"),
            FeatureSha = Require(values, "--feature-sha"),
            BaselineCommand = Require(values, "--baseline-command"),
            BaselineRepeatCommand = Require(values, "--baseline-repeat-command"),
            FeatureCommand = Require(values, "--feature-command"),
            RunnerSha256 = Require(values, "--runner-sha256"),
            BaselineHarnessPath = DirectoryPath(Require(values, "--baseline-harness")),
            FeatureHarnessPath = DirectoryPath(Require(values, "--feature-harness")),
            OutputPath = Path.GetFullPath(Require(values, "--output")),
            BootstrapIterations = PairedBenchmarkAnalyzer.DefaultBootstrapIterations,
        };
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option '{name}'. {Usage}");

    private static string FilePath(string value)
    {
        string path = Path.GetFullPath(value);
        return File.Exists(path) ? path : throw new FileNotFoundException("Required analyzer input is missing.", path);
    }

    private static string DirectoryPath(string value)
    {
        string path = Path.GetFullPath(value);
        return Directory.Exists(path) ? path : throw new DirectoryNotFoundException(path);
    }

    private const string Usage =
        "Usage: paired-analyze --baseline-results <json> --baseline-repeat-results <json> "
        + "--feature-results <json> --baseline-counters <dir> --baseline-repeat-counters <dir> "
        + "--feature-counters <dir> --baseline-stdout <file> --baseline-repeat-stdout <file> "
        + "--feature-stdout <file> --baseline-harness-provenance <json> "
        + "--baseline-repeat-harness-provenance <json> --feature-harness-provenance <json> "
        + "--baseline-harness-assembly <dll> --baseline-repeat-harness-assembly <dll> "
        + "--feature-harness-assembly <dll> "
        + "--baseline-outputs <dir> --baseline-repeat-outputs <dir> "
        + "--feature-outputs <dir> --baseline-sha <sha> --feature-sha <sha> "
        + "--baseline-command <command> --baseline-repeat-command <command> "
        + "--feature-command <command> --runner-sha256 <sha256> "
        + "--baseline-harness <dir> --feature-harness <dir> --output <json>";
}

internal sealed class PairedBenchmarkManifest
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; }
    public DateTimeOffset AnalyzedAtUtc { get; init; }
    public int BootstrapSeed { get; init; }
    public int BootstrapIterations { get; init; }
    public double ConfidenceLevel { get; init; }
    public string PrimaryCase { get; init; } = string.Empty;
    public string PrimaryAcceptanceRule { get; init; } = string.Empty;
    public bool PrimaryAcceptancePassed { get; init; }
    public string BaselineReferenceComposition { get; init; } = string.Empty;
    public string BaselineRepeatToleranceFormula { get; init; } = string.Empty;
    public string BaselineRepeatStabilityRule { get; init; } = string.Empty;
    public double MaximumBaselineRepeatSymmetricToleranceFactor { get; init; }
    public bool BaselineRepeatStable { get; init; }
    public string[] ControlBarrierCases { get; init; } = [];
    public string ControlBarrierAcceptanceRule { get; init; } = string.Empty;
    public bool ControlBarrierAcceptancePassed { get; init; }
    public bool OverallAcceptancePassed { get; init; }
    public PairedBenchmarkRunManifest Baseline { get; init; } = new();
    public PairedBenchmarkRunManifest BaselineRepeat { get; init; } = new();
    public PairedBenchmarkRunManifest Feature { get; init; } = new();
    public string RunnerSha256 { get; init; } = string.Empty;
    public string HarnessBuildInputBundleRule { get; init; } = string.Empty;
    public SortedDictionary<string, string> BaselineHarnessBuildInputSha256 { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> FeatureHarnessBuildInputSha256 { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, JsonElement> EnvironmentFingerprint { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, PairedBenchmarkCaseResult> Cases { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class PairedBenchmarkRunManifest
{
    public string CodeSha { get; init; } = string.Empty;
    public string EngineAssemblyVersion { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string BenchmarkDotNetResultFile { get; init; } = string.Empty;
    public string BenchmarkDotNetResultSha256 { get; init; } = string.Empty;
    public string StandardOutputFile { get; init; } = string.Empty;
    public string StandardOutputSha256 { get; init; } = string.Empty;
    public string CounterDirectory { get; init; } = string.Empty;
    public SortedDictionary<string, string> CounterFileSha256 { get; init; } = new(StringComparer.Ordinal);
    public string OutputDirectory { get; init; } = string.Empty;
    public SortedDictionary<string, string> OutputBlobFileSha256 { get; init; } = new(StringComparer.Ordinal);
    public string HarnessProvenanceFile { get; init; } = string.Empty;
    public string HarnessProvenanceSha256 { get; init; } = string.Empty;
    public string HarnessAssemblyName { get; init; } = string.Empty;
    public string HarnessAssemblyVersion { get; init; } = string.Empty;
    public string HarnessSourceRevision { get; init; } = string.Empty;
    public string HarnessAssemblySha256 { get; init; } = string.Empty;
    public string ExecutedHarnessAssemblyFile { get; init; } = string.Empty;
    public string ExecutedHarnessAssemblySha256 { get; init; } = string.Empty;
    public string HarnessBuildInputBundleSha256 { get; init; } = string.Empty;
    public SortedDictionary<string, string> BenchmarkDotNetArtifactSha256 { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class PairedBenchmarkCaseResult
{
    public int BaselineSampleCount { get; init; }
    public int BaselineFirstRunSampleCount { get; init; }
    public int BaselineRepeatSampleCount { get; init; }
    public int FeatureSampleCount { get; init; }
    public double BaselineMedianNanoseconds { get; init; }
    public double BaselineFirstRunMedianNanoseconds { get; init; }
    public double BaselineRepeatMedianNanoseconds { get; init; }
    public double BaselineRepeatMedianRatio { get; init; }
    public PairedConfidenceInterval BaselineRepeatConfidenceInterval95 { get; init; }
    public bool BaselineRepeatConfidenceContainsOne { get; init; }
    public double BaselineRepeatSymmetricToleranceFactor { get; init; }
    public PairedConfidenceInterval BaselineRepeatSymmetricToleranceInterval { get; init; }
    public bool BaselineRepeatStable { get; init; }
    public double FeatureMedianNanoseconds { get; init; }
    public double MedianRatio { get; init; }
    public PairedConfidenceInterval ConfidenceInterval95 { get; init; }
    public bool ConfidenceIntervalEntirelyBelowOne { get; init; }
    public bool IsControlOrBarrierGateCase { get; init; }
    public bool NoRegressionWithinBaselineRepeatTolerance { get; init; }
    public JsonElement BaselineCounters { get; init; }
    public JsonElement BaselineRepeatCounters { get; init; }
    public JsonElement FeatureCounters { get; init; }
}

internal readonly record struct PairedBootstrapResult(
    double MedianRatio,
    PairedConfidenceInterval ConfidenceInterval95);

internal readonly record struct PairedConfidenceInterval(double Lower, double Upper);

internal readonly record struct Rgba16fLocalizedParityMetrics(
    double MinimumSsim,
    double MaximumAlphaMae,
    double MaximumRgbaMae);

internal readonly record struct BaselineRepeatTolerance(
    double Factor,
    PairedConfidenceInterval Interval,
    bool ConfidenceContainsOne,
    bool Stable);

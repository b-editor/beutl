using System.Security.Cryptography;
using System.Text.Json;

namespace Beutl.Benchmarks.Rendering;

internal static class PairedBenchmarkAnalyzer
{
    internal const int DefaultBootstrapIterations = 100_000;
    private const int BootstrapSeed = RenderPipelineBenchmarkScenes.SourceSeed;
    private const string PrimaryCaseName = "ShaderOpacityShader";
    private const string SourceProvenanceField = "beutlEngineAssemblyVersion";
    private const double MaximumBaselineRepeatSymmetricToleranceFactor = 1.20;

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
            PairedBenchmarkManifest manifest = Analyze(options);
            string? parent = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            using (var stream = new FileStream(
                       options.OutputPath,
                       FileMode.Create,
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
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
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

        BenchmarkResultRun baselineResults = ReadBenchmarkResults(options.BaselineResultsPath);
        BenchmarkResultRun baselineRepeatResults = ReadBenchmarkResults(options.BaselineRepeatResultsPath);
        BenchmarkResultRun featureResults = ReadBenchmarkResults(options.FeatureResultsPath);
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
        ValidatePairedCounterContracts(baselineCounters, baselineRepeatCounters);
        ValidatePairedCounterContracts(baselineCounters, featureCounters);

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
            SchemaVersion = 2,
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
                options.BaselineResultsPath,
                options.BaselineStdoutPath,
                options.BaselineCountersPath,
                baselineCounters),
            BaselineRepeat = CreateRunManifest(
                options.BaselineSha,
                options.BaselineRepeatCommand,
                options.BaselineRepeatResultsPath,
                options.BaselineRepeatStdoutPath,
                options.BaselineRepeatCountersPath,
                baselineRepeatCounters),
            Feature = CreateRunManifest(
                options.FeatureSha,
                options.FeatureCommand,
                options.FeatureResultsPath,
                options.FeatureStdoutPath,
                options.FeatureCountersPath,
                featureCounters),
            RunnerSha256 = options.RunnerSha256,
            BaselineHarnessFileSha256 = HashDirectory(options.BaselineHarnessPath),
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
        string resultPath,
        string stdoutPath,
        string countersPath,
        CounterRun counters)
    {
        return new PairedBenchmarkRunManifest
        {
            CodeSha = sha,
            EngineAssemblyVersion = counters.SourceProvenance,
            Command = command,
            BenchmarkDotNetResultFile = Path.GetFileName(resultPath),
            BenchmarkDotNetResultSha256 = Sha256File(resultPath),
            StandardOutputFile = Path.GetFileName(stdoutPath),
            StandardOutputSha256 = Sha256File(stdoutPath),
            CounterDirectory = Path.GetFileName(Path.TrimEndingDirectorySeparator(countersPath)),
            CounterFileSha256 = counters.FileHashes,
            BenchmarkDotNetArtifactSha256 = HashDirectory(
                Path.GetDirectoryName(resultPath)
                ?? throw new InvalidDataException($"Benchmark result has no parent directory: {resultPath}")),
        };
    }

    private static BenchmarkResultRun ReadBenchmarkResults(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
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

    private static void ValidatePairedCounterContracts(CounterRun baseline, CounterRun feature)
    {
        foreach (string caseName in baseline.Cases.Keys)
        {
            IReadOnlyDictionary<string, string> baselineContract = baseline.Cases[caseName].Contract;
            IReadOnlyDictionary<string, string> featureContract = feature.Cases[caseName].Contract;
            string[] mismatches = baselineContract.Keys
                .Union(featureContract.Keys, StringComparer.Ordinal)
                .Where(key => !baselineContract.TryGetValue(key, out string? left)
                              || !featureContract.TryGetValue(key, out string? right)
                              || !string.Equals(left, right, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (mismatches.Length != 0)
            {
                throw new InvalidDataException(
                    $"Paired counter contract mismatch for '{caseName}': {string.Join(", ", mismatches)}.");
            }
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

    private static string Sha256File(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static SortedDictionary<string, string> HashDirectory(string directory)
    {
        string root = Path.GetFullPath(directory);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            result.Add(relative, Sha256File(path));
        }
        return result;
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
                if (schemaVersion != 2)
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
            foreach (JsonProperty property in fingerprint.EnumerateObject())
            {
                ValidateFingerprintValue(property, path);
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
            var contract = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in new[]
                     {
                         "seed", "width", "height", "setupWarmupFrames", "lifetime", "requestShape",
                     })
            {
                if (!root.TryGetProperty(name, out JsonElement value))
                    throw new InvalidDataException($"Counter contract field '{name}' is missing: {path}");
                contract.Add(name, CanonicalJson(value));
            }

            foreach (string name in new[] { "outputSha256", "outputChecksum" })
            {
                string text = root.GetProperty(name).GetString()
                    ?? throw new InvalidDataException($"Counter output field '{name}' is missing: {path}");
                if (string.IsNullOrWhiteSpace(text) || text.Any(static item => !Uri.IsHexDigit(item)))
                    throw new InvalidDataException($"Counter output field '{name}' is invalid: {path}");
            }

            foreach (string name in new[] { "setupLastRequestCounters", "measuredLastRequestCounters" })
            {
                JsonElement counters = root.GetProperty(name);
                if (counters.ValueKind != JsonValueKind.Object || !counters.EnumerateObject().Any())
                    throw new InvalidDataException($"Counter snapshot '{name}' is empty: {path}");
            }

            if (root.GetProperty("width").GetInt32() <= 0
                || root.GetProperty("height").GetInt32() <= 0
                || root.GetProperty("setupWarmupFrames").GetInt32() <= 0)
            {
                throw new InvalidDataException($"Counter dimensions or warm-up count are invalid: {path}");
            }
            return new CounterCase(root.Clone(), contract);
        }

        private static void ValidateFingerprintValue(JsonProperty property, string path)
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string? text = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(text)
                    || text.Contains("unknown", StringComparison.OrdinalIgnoreCase))
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
                                                || string.IsNullOrWhiteSpace(item.GetString())))
                {
                    throw new InvalidDataException(
                        $"Fingerprint array '{property.Name}' is empty or invalid in '{path}'.");
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
                JsonValueKind.Array => JsonSerializer.Serialize(
                    element.EnumerateArray().Select(static item => item.GetString()).ToArray()),
                _ => element.GetRawText(),
            };
        }

        private sealed record FingerprintParts(
            SortedDictionary<string, string> Environment,
            string SourceProvenance);
    }

    private sealed record CounterCase(
        JsonElement Record,
        SortedDictionary<string, string> Contract);

    private sealed record BenchmarkResultRun(
        SortedDictionary<string, double[]> Samples,
        string Method,
        string JobDisplay,
        string BenchmarkDotNetVersion);
}

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
    public required string BaselineSha { get; init; }
    public required string FeatureSha { get; init; }
    public required string BaselineCommand { get; init; }
    public required string BaselineRepeatCommand { get; init; }
    public required string FeatureCommand { get; init; }
    public required string RunnerSha256 { get; init; }
    public required string BaselineHarnessPath { get; init; }
    public required string OutputPath { get; init; }
    public int BootstrapIterations { get; init; }

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
            "--baseline-sha", "--feature-sha", "--baseline-command", "--baseline-repeat-command",
            "--feature-command", "--runner-sha256", "--output",
            "--baseline-harness", "--bootstrap-iterations",
        };
        string[] unknown = values.Keys.Where(key => !known.Contains(key)).ToArray();
        if (unknown.Length != 0)
            throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown)}. {Usage}");

        int iterations = PairedBenchmarkAnalyzer.DefaultBootstrapIterations;
        if (values.TryGetValue("--bootstrap-iterations", out string? iterationText)
            && !int.TryParse(
                iterationText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out iterations))
        {
            throw new ArgumentException("--bootstrap-iterations must be a positive integer.");
        }
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
            BaselineSha = Require(values, "--baseline-sha"),
            FeatureSha = Require(values, "--feature-sha"),
            BaselineCommand = Require(values, "--baseline-command"),
            BaselineRepeatCommand = Require(values, "--baseline-repeat-command"),
            FeatureCommand = Require(values, "--feature-command"),
            RunnerSha256 = Require(values, "--runner-sha256"),
            BaselineHarnessPath = DirectoryPath(Require(values, "--baseline-harness")),
            OutputPath = Path.GetFullPath(Require(values, "--output")),
            BootstrapIterations = iterations,
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
        + "--feature-stdout <file> --baseline-sha <sha> --feature-sha <sha> "
        + "--baseline-command <command> --baseline-repeat-command <command> "
        + "--feature-command <command> --runner-sha256 <sha256> "
        + "--baseline-harness <dir> --output <json> [--bootstrap-iterations <n>]";
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
    public SortedDictionary<string, string> BaselineHarnessFileSha256 { get; init; } = new(StringComparer.Ordinal);
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

internal readonly record struct BaselineRepeatTolerance(
    double Factor,
    PairedConfidenceInterval Interval,
    bool ConfidenceContainsOne,
    bool Stable);

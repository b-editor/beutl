using System.Security.Cryptography;
using System.Text.Json;

namespace Beutl.Evidence;

/// <summary>Builds the SC-008 manifest from baseline A, feature, and baseline B runs.</summary>
/// <remarks>
/// Baseline pooling requires the bootstrapped B/A interval to contain 1.0 within a symmetric factor of 1.20.
/// Resampling uses every original value without outlier removal or clipping.
/// </remarks>
public static class PairedBenchmarkAnalyzer
{
    /// <summary>The SC-008 base seed, combined with each case name's FNV-1a hash.</summary>
    public const int BaseSeed = 20040719;

    /// <summary>The fixed xor that separates the baseline-repeat resampling from the feature resampling.</summary>
    public const uint BaselineRepeatSeedXor = 0x9E3779B9u;

    public const int DefaultBootstrapIterations = 100_000;

    public const double ConfidenceLevel = 0.95;

    public const int RequiredSamplesPerRun = 15;

    public const double MaximumBaselineRepeatSymmetricToleranceFactor = 1.20;

    public const string PrimaryAcceptanceRule =
        "bootstrap-95%-ci-for-feature-over-pooled-stable-baseline-a-and-b-median-ratio-entirely-below-1.0";

    public const string BaselineReferenceComposition =
        "pooled-baseline-a-and-baseline-b-samples-after-repeat-stability-gate";

    public const string BaselineRepeatToleranceFormula =
        "factor=max(repeat-ci-upper,1/repeat-ci-lower); interval=[1/factor,factor]; no clipping";

    public const string BaselineRepeatStabilityRule =
        "repeat-95%-ci-must-contain-1.0-and-derived-symmetric-factor-must-be-at-most-1.20";

    public const string ControlBarrierAcceptanceRule =
        "feature-over-pooled-baseline-95%-ci-upper-at-most-case-specific-unclipped-repeat-tolerance-factor";

    public static PairedBenchmarkManifest Analyze(PairedBenchmarkAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        IReadOnlyDictionary<string, double[]> baselineA = request.BaselineA.Samples;
        IReadOnlyDictionary<string, double[]> feature = request.Feature.Samples;
        IReadOnlyDictionary<string, double[]> baselineB = request.BaselineB.Samples;

        string[] cases = request.Cases.Count > 0
            ? [.. request.Cases]
            : [.. baselineA.Keys.Intersect(feature.Keys, StringComparer.Ordinal)
                .Intersect(baselineB.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        if (cases.Length == 0)
            throw new InvalidOperationException("The three runs share no benchmark case.");

        var results = new SortedDictionary<string, PairedBenchmarkCaseResult>(StringComparer.Ordinal);
        var repeatIntervals = new Dictionary<string, ConfidenceInterval>(StringComparer.Ordinal);
        bool allRepeatsStable = true;

        // Pass 1: the repeat-stability gate. SC-008 requires every case to clear it before any pooling, so the
        // pooled analysis below cannot be reached by a case whose own baseline drifted.
        foreach (string caseName in cases)
        {
            double[] a = RequireSamples(baselineA, caseName, request.BaselineA.Label);
            double[] b = RequireSamples(baselineB, caseName, request.BaselineB.Label);
            uint repeatSeed = DeterministicBootstrapRandom.DeriveSeed(BaseSeed, caseName) ^ BaselineRepeatSeedXor;
            ConfidenceInterval interval = BootstrapMedianRatioInterval(
                b, a, repeatSeed, request.BootstrapIterations);
            repeatIntervals[caseName] = interval;
            if (!(IntervalContainsOne(interval) && SymmetricToleranceFactor(interval)
                    <= MaximumBaselineRepeatSymmetricToleranceFactor))
            {
                allRepeatsStable = false;
            }
        }

        bool primaryPassed = false;
        var controlCases = new HashSet<string>(request.ControlBarrierCases, StringComparer.Ordinal);

        // A declared control workload the run never measured proves nothing, so it fails the gate instead of
        // passing it vacuously.
        string[] missingControlCases =
            [.. controlCases.Except(cases, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        bool controlBarrierPassed = missingControlCases.Length == 0;

        foreach (string caseName in cases)
        {
            double[] a = RequireSamples(baselineA, caseName, request.BaselineA.Label);
            double[] b = RequireSamples(baselineB, caseName, request.BaselineB.Label);
            double[] f = RequireSamples(feature, caseName, request.Feature.Label);
            double[] pooled = [.. a, .. b];

            ConfidenceInterval repeat = repeatIntervals[caseName];
            double repeatFactor = SymmetricToleranceFactor(repeat);
            bool repeatStable = IntervalContainsOne(repeat)
                                && repeatFactor <= MaximumBaselineRepeatSymmetricToleranceFactor;

            uint seed = DeterministicBootstrapRandom.DeriveSeed(BaseSeed, caseName);
            ConfidenceInterval interval = BootstrapMedianRatioInterval(
                f, pooled, seed, request.BootstrapIterations);

            bool isControl = controlCases.Contains(caseName);
            bool belowOne = interval.Upper < 1.0;
            bool withinTolerance = interval.Upper <= repeatFactor;
            if (string.Equals(caseName, request.PrimaryCase, StringComparison.Ordinal))
                primaryPassed = allRepeatsStable && belowOne;
            if (isControl && !(allRepeatsStable && withinTolerance))
                controlBarrierPassed = false;

            results[caseName] = new PairedBenchmarkCaseResult
            {
                BaselineSampleCount = pooled.Length,
                BaselineFirstRunSampleCount = a.Length,
                BaselineRepeatSampleCount = b.Length,
                FeatureSampleCount = f.Length,
                BaselineMedianNanoseconds = Median(pooled),
                BaselineFirstRunMedianNanoseconds = Median(a),
                BaselineRepeatMedianNanoseconds = Median(b),
                BaselineRepeatMedianRatio = Median(b) / Median(a),
                BaselineRepeatConfidenceInterval95 = repeat,
                BaselineRepeatConfidenceContainsOne = IntervalContainsOne(repeat),
                BaselineRepeatSymmetricToleranceFactor = repeatFactor,
                BaselineRepeatSymmetricToleranceInterval =
                    new ConfidenceInterval { Lower = 1.0 / repeatFactor, Upper = repeatFactor },
                BaselineRepeatStable = repeatStable,
                FeatureMedianNanoseconds = Median(f),
                MedianRatio = Median(f) / Median(pooled),
                ConfidenceInterval95 = interval,
                ConfidenceIntervalEntirelyBelowOne = belowOne,
                IsControlOrBarrierGateCase = isControl,
                NoRegressionWithinBaselineRepeatTolerance = withinTolerance,
            };
        }

        if (!results.ContainsKey(request.PrimaryCase))
        {
            throw new InvalidOperationException(
                $"The primary case '{request.PrimaryCase}' is not present in all three runs.");
        }

        (bool comparable, string? mismatch, RenderEvidenceFingerprint? fingerprint) = CompareFingerprints(request);

        bool overall = allRepeatsStable && primaryPassed && controlBarrierPassed && comparable;
        return new PairedBenchmarkManifest
        {
            AnalyzedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ComparisonMode = request.ComparisonMode,
            BootstrapSeed = BaseSeed,
            BootstrapIterations = request.BootstrapIterations,
            ConfidenceLevel = ConfidenceLevel,
            PrimaryCase = request.PrimaryCase,
            PrimaryAcceptanceRule = PrimaryAcceptanceRule,
            PrimaryAcceptancePassed = primaryPassed,
            BaselineReferenceComposition = BaselineReferenceComposition,
            BaselineRepeatToleranceFormula = BaselineRepeatToleranceFormula,
            BaselineRepeatStabilityRule = BaselineRepeatStabilityRule,
            MaximumBaselineRepeatSymmetricToleranceFactor = MaximumBaselineRepeatSymmetricToleranceFactor,
            BaselineRepeatStable = allRepeatsStable,
            ControlBarrierCases = [.. request.ControlBarrierCases.Order(StringComparer.Ordinal)],
            MissingControlBarrierCases = missingControlCases,
            ControlBarrierAcceptanceRule = ControlBarrierAcceptanceRule,
            ControlBarrierAcceptancePassed = controlBarrierPassed,
            FingerprintsComparable = comparable,
            FingerprintMismatchReason = mismatch,
            OverallAcceptancePassed = overall,
            BaselineA = request.BaselineA.Provenance,
            Feature = request.Feature.Provenance,
            BaselineB = request.BaselineB.Provenance,
            EnvironmentFingerprint = fingerprint,
            Cases = results,
        };
    }

    /// <summary>
    /// The linearly interpolated 95% interval of <c>median(numerator resample) / median(denominator resample)</c>.
    /// </summary>
    public static ConfidenceInterval BootstrapMedianRatioInterval(
        double[] numeratorSamples,
        double[] denominatorSamples,
        uint seed,
        int iterations)
    {
        ArgumentNullException.ThrowIfNull(numeratorSamples);
        ArgumentNullException.ThrowIfNull(denominatorSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        if (numeratorSamples.Length == 0 || denominatorSamples.Length == 0)
            throw new ArgumentException("A bootstrap needs at least one sample on each side.");

        var random = new DeterministicBootstrapRandom(seed);
        double[] ratios = new double[iterations];
        double[] numeratorResample = new double[numeratorSamples.Length];
        double[] denominatorResample = new double[denominatorSamples.Length];
        for (int index = 0; index < iterations; index++)
        {
            for (int item = 0; item < numeratorResample.Length; item++)
                numeratorResample[item] = numeratorSamples[random.NextIndex(numeratorSamples.Length)];
            for (int item = 0; item < denominatorResample.Length; item++)
                denominatorResample[item] = denominatorSamples[random.NextIndex(denominatorSamples.Length)];
            ratios[index] = Median(numeratorResample) / Median(denominatorResample);
        }

        Array.Sort(ratios);
        const double tail = (1.0 - ConfidenceLevel) / 2.0;
        return new ConfidenceInterval
        {
            Lower = InterpolatedPercentile(ratios, tail),
            Upper = InterpolatedPercentile(ratios, 1.0 - tail),
        };
    }

    /// <summary>The <paramref name="fraction"/> quantile of an ascending array, interpolated between ranks.</summary>
    public static double InterpolatedPercentile(double[] ascending, double fraction)
    {
        ArgumentNullException.ThrowIfNull(ascending);
        if (ascending.Length == 0)
            throw new ArgumentException("A percentile needs at least one value.", nameof(ascending));
        if (ascending.Length == 1)
            return ascending[0];

        double rank = fraction * (ascending.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return ascending[lower];
        double weight = rank - lower;
        return (ascending[lower] * (1.0 - weight)) + (ascending[upper] * weight);
    }

    /// <summary>The median of <paramref name="values"/>, which this method sorts in place.</summary>
    public static double Median(double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("A median needs at least one value.", nameof(values));
        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
    }

    public static bool IntervalContainsOne(ConfidenceInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return interval.Lower <= 1.0 && interval.Upper >= 1.0;
    }

    /// <summary>The unclipped symmetric drift factor a baseline's own repeat interval licenses.</summary>
    public static double SymmetricToleranceFactor(ConfidenceInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (interval.Lower <= 0)
            throw new InvalidOperationException("A baseline repeat interval must have a positive lower bound.");
        return Math.Max(interval.Upper, 1.0 / interval.Lower);
    }

    private static double[] RequireSamples(
        IReadOnlyDictionary<string, double[]> samples,
        string caseName,
        string label)
    {
        if (!samples.TryGetValue(caseName, out double[]? values))
            throw new InvalidOperationException($"Run '{label}' has no samples for case '{caseName}'.");
        if (values.Length != RequiredSamplesPerRun)
        {
            throw new InvalidOperationException(
                $"Run '{label}' case '{caseName}' supplied {values.Length} samples; SC-008 requires exactly "
                + $"{RequiredSamplesPerRun}.");
        }

        foreach (double value in values)
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new InvalidOperationException(
                    $"Run '{label}' case '{caseName}' contains a non-finite or non-positive sample.");
            }
        }

        return [.. values];
    }

    private static (bool Comparable, string? Reason, RenderEvidenceFingerprint? Fingerprint) CompareFingerprints(
        PairedBenchmarkAnalysisRequest request)
    {
        RenderEvidenceFingerprint?[] fingerprints =
        [
            request.BaselineA.Fingerprint,
            request.Feature.Fingerprint,
            request.BaselineB.Fingerprint,
        ];
        if (Array.Exists(fingerprints, static item => item is null))
        {
            return (false,
                "At least one run did not record an environment fingerprint, so the runs cannot be shown "
                + "to be comparable.",
                null);
        }

        RenderEvidenceFingerprint first = fingerprints[0]!;
        for (int index = 1; index < fingerprints.Length; index++)
        {
            if (!first.IsComparableTo(fingerprints[index]))
            {
                return (false,
                    "The runs were produced under different conditions: comparability key "
                    + $"{first.ComparabilityKey} does not match {fingerprints[index]!.ComparabilityKey}.",
                    null);
            }
        }

        return (true, null, first);
    }

    internal static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>One of the three runs a paired analysis consumes.</summary>
public sealed record PairedBenchmarkRun
{
    public required string Label { get; init; }

    public required IReadOnlyDictionary<string, double[]> Samples { get; init; }

    public RenderEvidenceFingerprint? Fingerprint { get; init; }

    public PairedBenchmarkRunProvenance? Provenance { get; init; }

    /// <summary>
    /// Reads a run directory holding exactly one BenchmarkDotNet <c>*-report-full.json</c> and an optional
    /// <c>counters/</c> directory written by <c>RenderPipelineBenchmarks</c>.
    /// </summary>
    public static PairedBenchmarkRun FromDirectory(string label, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Run directory '{full}' does not exist.");

        string[] reports = Directory.GetFiles(full, "*-report-full.json", SearchOption.AllDirectories);
        if (reports.Length != 1)
        {
            throw new InvalidOperationException(
                $"Run directory '{full}' holds {reports.Length} BenchmarkDotNet full reports; exactly one is "
                + "required so the analysis cannot silently pick a stale one.");
        }

        string report = reports[0];
        IReadOnlyDictionary<string, double[]> samples = ReadSamples(report);

        var counterHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        RenderEvidenceFingerprint? fingerprint = null;
        string? fusionMode = null;
        string? engineVersion = null;
        string countersDirectory = Path.Combine(full, "counters");
        if (Directory.Exists(countersDirectory))
        {
            foreach (string counter in Directory.GetFiles(countersDirectory, "*.json").Order(StringComparer.Ordinal))
            {
                counterHashes[Path.GetFileName(counter)] = PairedBenchmarkAnalyzer.Sha256File(counter);
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(counter));
                if (fingerprint is null
                    && document.RootElement.TryGetProperty("fingerprint", out JsonElement element)
                    && element.ValueKind == JsonValueKind.Object)
                {
                    fingerprint = element.Deserialize<RenderEvidenceFingerprint>(
                        RenderEvidenceFingerprint.JsonOptions);
                }

                if (fusionMode is null
                    && document.RootElement.TryGetProperty("fusionMode", out JsonElement mode)
                    && mode.ValueKind == JsonValueKind.String)
                {
                    fusionMode = mode.GetString();
                }
            }

            engineVersion = fingerprint?.BeutlEngineAssemblyVersion;
        }

        return new PairedBenchmarkRun
        {
            Label = label,
            Samples = samples,
            Fingerprint = fingerprint,
            Provenance = new PairedBenchmarkRunProvenance
            {
                Label = label,
                RunDirectoryName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)),
                BenchmarkDotNetResultFile = Path.GetRelativePath(full, report),
                BenchmarkDotNetResultSha256 = PairedBenchmarkAnalyzer.Sha256File(report),
                CounterFileSha256 = counterHashes,
                EngineAssemblyVersion = engineVersion,
                EngineSourceRevision = fingerprint?.BeutlEngineSourceRevision,
                FusionMode = fusionMode,
                ComparabilityKey = fingerprint?.ComparabilityKey,
            },
        };
    }

    /// <summary>Extracts each case's raw <c>Statistics.OriginalValues</c> from a BenchmarkDotNet full report.</summary>
    public static IReadOnlyDictionary<string, double[]> ReadSamples(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));
        if (!document.RootElement.TryGetProperty("Benchmarks", out JsonElement benchmarks))
            throw new InvalidOperationException($"'{reportPath}' has no Benchmarks array.");

        var result = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (JsonElement benchmark in benchmarks.EnumerateArray())
        {
            string caseName = ReadCaseName(benchmark);
            if (!benchmark.TryGetProperty("Statistics", out JsonElement statistics)
                || statistics.ValueKind != JsonValueKind.Object
                || !statistics.TryGetProperty("OriginalValues", out JsonElement values))
            {
                throw new InvalidOperationException($"'{reportPath}' case '{caseName}' has no OriginalValues.");
            }

            double[] samples = [.. values.EnumerateArray().Select(static item => item.GetDouble())];
            if (!result.TryAdd(caseName, samples))
                throw new InvalidOperationException($"'{reportPath}' reports case '{caseName}' more than once.");
        }

        return result;
    }

    /// <summary>Reads a record's case name from <c>FullName</c>, falling back to <c>Parameters</c>.</summary>
    /// <remarks>
    /// <c>Parameters</c> is display text and BenchmarkDotNet abbreviates a long value into the form
    /// <c>Multi(...)ncies [35]</c>, which would silently key the analysis by a name no scene has.
    /// <c>FullName</c> carries the value verbatim as <c>Method(CaseName: "…")</c>.
    /// </remarks>
    internal static string ReadCaseName(JsonElement benchmark)
    {
        if (benchmark.TryGetProperty("FullName", out JsonElement fullName)
            && fullName.ValueKind == JsonValueKind.String
            && fullName.GetString() is { Length: > 0 } full
            && ExtractQuotedParameter(full, "CaseName") is { } fromFullName)
        {
            return fromFullName;
        }

        if (benchmark.TryGetProperty("Parameters", out JsonElement parameters)
            && parameters.ValueKind == JsonValueKind.String
            && parameters.GetString() is { Length: > 0 } text)
        {
            foreach (string pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0 && pair.AsSpan(0, separator).Trim().SequenceEqual("CaseName"))
                {
                    string value = pair[(separator + 1)..].Trim();
                    if (value.Contains("(...)", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"BenchmarkDotNet abbreviated the case name to '{value}' and the record carries no "
                            + "usable FullName, so the analysis cannot identify the workload.");
                    }

                    return value;
                }
            }
        }

        return benchmark.TryGetProperty("Method", out JsonElement method) && method.GetString() is { } name
            ? name
            : throw new InvalidOperationException("A benchmark record has neither a CaseName parameter nor a Method.");
    }

    /// <summary>Extracts <c>name: "value"</c> from a BenchmarkDotNet full name.</summary>
    private static string? ExtractQuotedParameter(string fullName, string name)
    {
        string marker = name + ": \"";
        int start = fullName.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += marker.Length;
        int end = fullName.IndexOf('"', start);
        return end > start ? fullName[start..end] : null;
    }
}

/// <summary>Everything an analysis needs, so the manifest records the rules it was judged by.</summary>
public sealed record PairedBenchmarkAnalysisRequest
{
    public required PairedBenchmarkRun BaselineA { get; init; }
    public required PairedBenchmarkRun Feature { get; init; }
    public required PairedBenchmarkRun BaselineB { get; init; }
    public required string PrimaryCase { get; init; }
    public required string ComparisonMode { get; init; }

    public IReadOnlyList<string> ControlBarrierCases { get; init; } = [];

    /// <summary>Restricts the analysis to these cases; empty means every case all three runs share.</summary>
    public IReadOnlyList<string> Cases { get; init; } = [];

    public int BootstrapIterations { get; init; } = PairedBenchmarkAnalyzer.DefaultBootstrapIterations;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PrimaryCase);
        ArgumentException.ThrowIfNullOrWhiteSpace(ComparisonMode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BootstrapIterations);
    }
}

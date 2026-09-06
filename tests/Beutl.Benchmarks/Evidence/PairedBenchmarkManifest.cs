using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Evidence;

/// <summary>The committed artifact a paired SC-008 run produces.</summary>
public sealed record PairedBenchmarkManifest
{
    public const int CurrentSchemaVersion = 3;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string AnalyzedAtUtc { get; init; } = string.Empty;

    /// <summary>What the two sides of this comparison actually are.</summary>
    /// <remarks>
    /// SC-008 is written against a pre-feature baseline. A run whose baseline is this branch's renderer with
    /// fusion disabled measures the fusion optimization inside this feature's renderer and is a strictly weaker
    /// claim, so the mode is recorded rather than assumed.
    /// </remarks>
    public string ComparisonMode { get; init; } = string.Empty;

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

    /// <summary>Declared control or barrier workloads this run never measured, which fail the gate.</summary>
    public string[] MissingControlBarrierCases { get; init; } = [];

    public string ControlBarrierAcceptanceRule { get; init; } = string.Empty;
    public bool ControlBarrierAcceptancePassed { get; init; }

    public bool FingerprintsComparable { get; init; }
    public string? FingerprintMismatchReason { get; init; }

    public bool OverallAcceptancePassed { get; init; }

    public PairedBenchmarkRunProvenance? BaselineA { get; init; }
    public PairedBenchmarkRunProvenance? Feature { get; init; }
    public PairedBenchmarkRunProvenance? BaselineB { get; init; }

    public RenderEvidenceFingerprint? EnvironmentFingerprint { get; init; }

    public SortedDictionary<string, PairedBenchmarkCaseResult> Cases { get; init; } =
        new(StringComparer.Ordinal);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";
}

/// <summary>Where one side of a paired run came from.</summary>
public sealed record PairedBenchmarkRunProvenance
{
    public string Label { get; init; } = string.Empty;

    /// <summary>The run directory's name, not its path.</summary>
    /// <remarks>
    /// The absolute path is machine-local and usually a temporary directory that will not survive, so it would
    /// be provenance pointing at nothing. What identifies the run is the report and counter hashes below.
    /// </remarks>
    public string RunDirectoryName { get; init; } = string.Empty;

    public string BenchmarkDotNetResultFile { get; init; } = string.Empty;
    public string BenchmarkDotNetResultSha256 { get; init; } = string.Empty;
    public SortedDictionary<string, string> CounterFileSha256 { get; init; } = new(StringComparer.Ordinal);
    public string? EngineAssemblyVersion { get; init; }
    public string? EngineSourceRevision { get; init; }
    public string? FusionMode { get; init; }
    public string? ComparabilityKey { get; init; }
}

/// <summary>Every number SC-008 requires the manifest to record for one workload.</summary>
public sealed record PairedBenchmarkCaseResult
{
    public int BaselineSampleCount { get; init; }
    public int BaselineFirstRunSampleCount { get; init; }
    public int BaselineRepeatSampleCount { get; init; }
    public int FeatureSampleCount { get; init; }

    public double BaselineMedianNanoseconds { get; init; }
    public double BaselineFirstRunMedianNanoseconds { get; init; }
    public double BaselineRepeatMedianNanoseconds { get; init; }
    public double BaselineRepeatMedianRatio { get; init; }
    public ConfidenceInterval BaselineRepeatConfidenceInterval95 { get; init; } = new();
    public bool BaselineRepeatConfidenceContainsOne { get; init; }
    public double BaselineRepeatSymmetricToleranceFactor { get; init; }
    public ConfidenceInterval BaselineRepeatSymmetricToleranceInterval { get; init; } = new();
    public bool BaselineRepeatStable { get; init; }

    public double FeatureMedianNanoseconds { get; init; }
    public double MedianRatio { get; init; }
    public ConfidenceInterval ConfidenceInterval95 { get; init; } = new();
    public bool ConfidenceIntervalEntirelyBelowOne { get; init; }

    public bool IsControlOrBarrierGateCase { get; init; }
    public bool NoRegressionWithinBaselineRepeatTolerance { get; init; }
}

/// <summary>A closed interval reported by the bootstrap.</summary>
public sealed record ConfidenceInterval
{
    public double Lower { get; init; }
    public double Upper { get; init; }
}

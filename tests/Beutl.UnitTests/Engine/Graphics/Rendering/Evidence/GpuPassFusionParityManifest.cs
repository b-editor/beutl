using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Evidence;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

/// <summary>The SC-007 artifact: what was compared, on what device, at what commit, and with what result.</summary>
internal sealed record GpuPassFusionParityManifest
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The comparison the in-tree harness performs.</summary>
    /// <remarks>
    /// Both sides run in one process on one device, so neither needs a committed device-specific oracle. This
    /// is not a comparison against a pre-feature build; a run that does that records
    /// <see cref="DifferentialAgainstTargetCommitMode"/> instead.
    /// </remarks>
    public const string SameProcessFusionMode = "same-process-fusion-disabled-vs-enabled";

    /// <summary>The comparison SC-007 names, which needs a second build of the same corpus on the same device.</summary>
    public const string DifferentialAgainstTargetCommitMode = "differential-against-target-commit";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string GeneratedAtUtc { get; init; } = string.Empty;

    public string ComparisonMode { get; init; } = SameProcessFusionMode;

    /// <summary>The commit the compared build came from, when the assembly version carries one.</summary>
    public string BeutlEngineSourceRevision { get; init; } = string.Empty;

    public string BeutlEngineAssemblyVersion { get; init; } = string.Empty;

    public GpuPassFusionParityThresholds Thresholds { get; init; } = new();

    public RenderEvidenceFingerprint? EnvironmentFingerprint { get; init; }

    public string? FingerprintUnavailableReason { get; init; }

    public int CaseCount { get; init; }

    public int PassedCaseCount { get; init; }

    /// <summary>Whether every recorded case met every applicable threshold.</summary>
    public bool AllCasesPassed { get; init; }

    public SortedDictionary<string, GpuPassFusionParityCase> Cases { get; init; } = new(StringComparer.Ordinal);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";
}

/// <summary>The fixed bounds every recorded case is judged against.</summary>
internal sealed record GpuPassFusionParityThresholds
{
    public double MinimumSsim { get; init; }
    public double MinimumWindowedSsim { get; init; }
    public double MaximumLinearRgbMae { get; init; }
    public double MaximumAlphaMae { get; init; }
    public double MaximumAaEdgeChannelError { get; init; }
    public double MaximumAaEdgeMeanError { get; init; }
}

/// <summary>One compared content item.</summary>
internal sealed record GpuPassFusionParityCase
{
    public string CaseName { get; init; } = string.Empty;
    public double OutputScale { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public double Ssim { get; init; }
    public double WindowedSsim { get; init; }
    public double LinearRgbMae { get; init; }
    public double AlphaMae { get; init; }

    public string? AaEdgeRegion { get; init; }
    public double? AaEdgeCropSsim { get; init; }
    public double? AaEdgeCropWindowedSsim { get; init; }
    public double? AaEdgeCropLinearRgbMae { get; init; }
    public double? AaEdgeCropAlphaMae { get; init; }
    public double? AaEdgeBandMeanError { get; init; }
    public double? AaEdgeMaximumRedError { get; init; }
    public double? AaEdgeMaximumGreenError { get; init; }
    public double? AaEdgeMaximumBlueError { get; init; }
    public double? AaEdgeMaximumAlphaError { get; init; }

    public bool Passed { get; init; }
}

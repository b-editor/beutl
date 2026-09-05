using System.Reflection;

using Beutl.Evidence;
using Beutl.Graphics.Backend;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

/// <summary>Accumulates same-process parity results into an SC-007 manifest.</summary>
/// <remarks>
/// Rewrites after every case so a crashed suite still leaves a self-describing partial manifest.
/// </remarks>
internal sealed class GpuPassFusionParityManifestBuilder
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, GpuPassFusionParityCase> _cases = new(StringComparer.Ordinal);
    private RenderEvidenceFingerprint? _fingerprint;
    private string? _fingerprintUnavailableReason;

    public GpuPassFusionParityManifestBuilder(string comparisonMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonMode);
        ComparisonMode = comparisonMode;
    }

    public string ComparisonMode { get; }

    public void SetFingerprint(RenderEvidenceFingerprint? fingerprint, string? unavailableReason)
    {
        lock (_gate)
        {
            _fingerprint = fingerprint;
            _fingerprintUnavailableReason = unavailableReason;
        }
    }

    public void Add(GpuPassFusionParityCase value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            // A parameterized fixture can compare the same case name more than once. Keeping the worst result
            // stops a later lenient repetition from overwriting an earlier failure in the artifact.
            if (_cases.TryGetValue(value.CaseName, out GpuPassFusionParityCase? existing))
            {
                bool replacementIsWorse = (existing.Passed && !value.Passed)
                                          || (existing.Passed == value.Passed && value.Ssim < existing.Ssim);
                if (!replacementIsWorse)
                    return;
            }

            _cases[value.CaseName] = value;
        }
    }

    public GpuPassFusionParityManifest Build()
    {
        lock (_gate)
        {
            string engineVersion = typeof(Beutl.Graphics.Rendering.RenderNode).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "no-informational-version";
            int passed = _cases.Values.Count(static item => item.Passed);
            return new GpuPassFusionParityManifest
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                ComparisonMode = ComparisonMode,
                BeutlEngineAssemblyVersion = engineVersion,
                BeutlEngineSourceRevision = RenderEvidenceFingerprint.ExtractSourceRevision(engineVersion),
                Thresholds = new GpuPassFusionParityThresholds
                {
                    MinimumSsim = GpuPassFusionSameProcessParityHarness.MinimumSsim,
                    MinimumWindowedSsim = GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim,
                    MaximumLinearRgbMae = GpuPassFusionSameProcessParityHarness.MaximumLinearRgbMae,
                    MaximumAlphaMae = GpuPassFusionSameProcessParityHarness.MaximumAlphaMae,
                    MaximumAaEdgeChannelError = GpuPassFusionSameProcessParityHarness.MaximumAaEdgeChannelError,
                    MaximumAaEdgeMeanError = GpuPassFusionSameProcessParityHarness.MaximumAaEdgeMeanError,
                },
                EnvironmentFingerprint = _fingerprint,
                FingerprintUnavailableReason = _fingerprintUnavailableReason,
                CaseCount = _cases.Count,
                PassedCaseCount = passed,
                AllCasesPassed = _cases.Count > 0 && passed == _cases.Count,
                Cases = new SortedDictionary<string, GpuPassFusionParityCase>(_cases, StringComparer.Ordinal),
            };
        }
    }

    /// <summary>Turns a harness result into the manifest's per-case shape, deciding pass from the thresholds.</summary>
    public static GpuPassFusionParityCase CreateCase(
        string caseName,
        double outputScale,
        int width,
        int height,
        GpuPassFusionParityResult result,
        PixelRect? aaEdgeRegion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseName);
        GpuPassFusionParityMetrics full = result.FullImage;
        bool passed = full.Ssim >= GpuPassFusionSameProcessParityHarness.MinimumSsim
                      && full.WindowedSsim >= GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim
                      && full.LinearRgbMae <= GpuPassFusionSameProcessParityHarness.MaximumLinearRgbMae
                      && full.AlphaMae <= GpuPassFusionSameProcessParityHarness.MaximumAlphaMae;
        GpuPassFusionAaParityMetrics? edge = result.AaEdge;
        if (edge is { } value)
        {
            passed = passed
                     && value.Crop.Ssim >= GpuPassFusionSameProcessParityHarness.MinimumSsim
                     && value.Crop.WindowedSsim >= GpuPassFusionSameProcessParityHarness.MinimumWindowedSsim
                     && value.Crop.LinearRgbMae <= GpuPassFusionSameProcessParityHarness.MaximumLinearRgbMae
                     && value.Crop.AlphaMae <= GpuPassFusionSameProcessParityHarness.MaximumAlphaMae
                     && value.EdgeBandMeanError <= GpuPassFusionSameProcessParityHarness.MaximumAaEdgeMeanError
                     && value.MaximumError.Maximum <= GpuPassFusionSameProcessParityHarness.MaximumAaEdgeChannelError;
        }

        return new GpuPassFusionParityCase
        {
            CaseName = caseName,
            OutputScale = outputScale,
            Width = width,
            Height = height,
            Ssim = full.Ssim,
            WindowedSsim = full.WindowedSsim,
            LinearRgbMae = full.LinearRgbMae,
            AlphaMae = full.AlphaMae,
            AaEdgeRegion = aaEdgeRegion is { } region
                ? $"{region.X}, {region.Y}, {region.Width}, {region.Height}"
                : null,
            AaEdgeCropSsim = edge?.Crop.Ssim,
            AaEdgeCropWindowedSsim = edge?.Crop.WindowedSsim,
            AaEdgeCropLinearRgbMae = edge?.Crop.LinearRgbMae,
            AaEdgeCropAlphaMae = edge?.Crop.AlphaMae,
            AaEdgeBandMeanError = edge?.EdgeBandMeanError,
            AaEdgeMaximumRedError = edge?.MaximumError.Red,
            AaEdgeMaximumGreenError = edge?.MaximumError.Green,
            AaEdgeMaximumBlueError = edge?.MaximumError.Blue,
            AaEdgeMaximumAlphaError = edge?.MaximumError.Alpha,
            Passed = passed,
        };
    }
}

/// <summary>
/// The environment-driven facade the parity harness reports into.
/// </summary>
/// <remarks>
/// Recording is opt-in through <see cref="OutputPathEnvironmentVariable"/> so an ordinary CI run neither writes
/// files nor pays for a fingerprint capture, and so the evidence artifact only ever comes from a run that was
/// asked to produce one.
/// </remarks>
internal static class GpuPassFusionParityEvidence
{
    public const string OutputPathEnvironmentVariable = "BEUTL_GPU_PASS_FUSION_PARITY_MANIFEST";

    private static readonly Lazy<string?> s_outputPath = new(static () =>
    {
        string? value = Environment.GetEnvironmentVariable(OutputPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    });

    private static readonly Lazy<GpuPassFusionParityManifestBuilder> s_builder = new(static () =>
    {
        var builder = new GpuPassFusionParityManifestBuilder(
            GpuPassFusionParityManifest.SameProcessFusionMode);
        RenderEvidenceFingerprint? fingerprint = RenderEvidenceFingerprint.TryCapture(
            GraphicsContextFactory.SharedContext,
            out string? reason);
        builder.SetFingerprint(fingerprint, reason);
        return builder;
    });

    public static string? OutputPath => s_outputPath.Value;

    public static bool IsRecording => OutputPath is not null;

    public static void Record(
        string caseName,
        double outputScale,
        int width,
        int height,
        GpuPassFusionParityResult result,
        PixelRect? aaEdgeRegion)
    {
        if (OutputPath is not { } path)
            return;

        GpuPassFusionParityManifestBuilder builder = s_builder.Value;
        builder.Add(GpuPassFusionParityManifestBuilder.CreateCase(
            caseName, outputScale, width, height, result, aaEdgeRegion));
        Write(builder, path);
    }

    private static void Write(GpuPassFusionParityManifestBuilder builder, string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } parent)
            Directory.CreateDirectory(parent);
        File.WriteAllText(path, builder.Build().ToJson());
    }
}

using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

[McpServerToolType]
public sealed class RenderTools(
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard,
    StillRenderer stillRenderer,
    MotionVariationAnalyzer motionVariationAnalyzer,
    QualityAnalyzer qualityAnalyzer,
    VideoExporter videoExporter) : ToolBase
{
    [McpServerTool(Name = "render_still")]
    [Description("Renders a still PNG from the current scene to a workspace-relative output path and returns visibility warnings for blank or near-black frames. Bare filenames are written under agent-output/.")]
    public ValueTask<ToolResult<RenderStillResponse>> RenderStill(
        [Description("Workspace-relative or in-workspace absolute output path. Bare filenames are written under agent-output/. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Scene time in seconds.")]
        double timeSeconds = 0,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            Scene scene = RequireScene();
            string resolvedPath = workspace.ResolveForWrite(NormalizeOutputPath(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            return await stillRenderer.RenderAsync(
                scene,
                TimeSpan.FromSeconds(Math.Max(0, timeSeconds)),
                resolvedPath,
                renderScale,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "evaluate_motion_variation")]
    [Description("Renders multiple in-memory samples from the current scene and reports time-series pixel differences plus frame-coverage checks. Use after render_still to catch motion graphics that have visible stills but too little temporal change or content confined to one quadrant.")]
    public ValueTask<ToolResult<MotionVariationResponse>> EvaluateMotionVariation(
        [Description("Optional explicit scene times in seconds. When omitted, the tool samples evenly across the scene duration.")]
        double[]? timeSeconds = null,
        [Description("Number of evenly spaced samples when timeSeconds is omitted. Clamped to 2..8.")]
        int sampleCount = 5,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Minimum changed-pixel ratio required for every adjacent sample pair. Defaults to 0.02.")]
        double minChangedPixelRatio = 0.02,
        [Description("Per-pixel absolute channel delta threshold. Higher values ignore subtle noise. Defaults to 48.")]
        int pixelDeltaThreshold = 48,
        [Description("Minimum occupied bounds ratio before repeated one-quadrant framing is treated as poor coverage. Defaults to 0.35.")]
        double minOccupiedBoundsRatio = 0.35,
        [Description("Maximum allowed foreground share in a single quadrant when occupied bounds are small. Defaults to 0.90.")]
        double maxSingleQuadrantForegroundRatio = 0.90,
        [Description("Per-channel luma threshold used to decide whether a pixel is visible foreground for coverage checks. Defaults to 24.")]
        int foregroundLumaThreshold = 24,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            Scene scene = RequireScene();
            IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
            return await motionVariationAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                renderScale,
                minChangedPixelRatio,
                pixelDeltaThreshold,
                minOccupiedBoundsRatio,
                maxSingleQuadrantForegroundRatio,
                foregroundLumaThreshold,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "evaluate_edit_quality")]
    [Description("Reviews the current scene for deterministic AI-editing quality risks: all-caps typography, visual hierarchy overload, short text read time, RectShape overuse, text backing alignment, palette problems, arbitrary dense effect stacks, dated card/shadow styling, low motion continuity, and chopped-up cut rhythm. Run after render_still and evaluate_motion_variation; resolve critical/major issues before export_video.")]
    public ValueTask<ToolResult<QualityReviewResponse>> EvaluateEditQuality(
        [Description("Optional explicit scene times in seconds for rendered motion checks. When omitted, samples evenly across the scene duration.")]
        double[]? timeSeconds = null,
        [Description("Number of evenly spaced samples when timeSeconds is omitted. Clamped to 2..8.")]
        int sampleCount = 5,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Optional profile label recorded in the review notes, such as draft, editorial, kinetic-type, or minimal.")]
        string? styleProfile = null,
        [Description("When true, long all-caps text is downgraded from major to minor instead of blocking the quality gate.")]
        bool allowAllCaps = false,
        [Description("When true, repeated hard-cut-like timing boundaries are not treated as major quality issues.")]
        bool allowHardCuts = false,
        [Description("When true, non-background RectShape dominance is not treated as a major quality issue.")]
        bool allowRectDominance = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            Scene scene = RequireScene();
            IReadOnlyList<TimeSpan>? sampleTimes = timeSeconds is { Length: > 0 }
                ? timeSeconds
                    .Where(double.IsFinite)
                    .Select(seconds => TimeSpan.FromSeconds(Math.Max(0, seconds)))
                    .ToArray()
                : null;
            return await qualityAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                sampleCount,
                renderScale,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                evaluateMotion: true,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "export_video")]
    [Description("Exports the current scene through a registered headless encoder to a workspace-relative output path. Bare filenames are written under agent-output/.")]
    public ValueTask<ToolResult<ExportVideoResponse>> ExportVideo(
        [Description("Workspace-relative or in-workspace absolute output path. Bare filenames are written under agent-output/. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Frame-rate numerator.")]
        int frameRateNumerator = 30,
        [Description("Frame-rate denominator.")]
        int frameRateDenominator = 1,
        [Description("Audio sample rate.")]
        int sampleRate = 44100,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            if (frameRateNumerator <= 0 || frameRateDenominator <= 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "Frame-rate numerator and denominator must be positive."));
            }

            Scene scene = RequireScene();
            string resolvedPath = workspace.ResolveForWrite(NormalizeOutputPath(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            return await videoExporter.ExportAsync(
                scene,
                resolvedPath,
                new Rational(frameRateNumerator, frameRateDenominator),
                sampleRate,
                renderScale,
                cancellationToken).ConfigureAwait(false);
        });
    }

    public static string NormalizeOutputPath(string outputPath)
    {
        if (Path.IsPathRooted(outputPath)
            || !string.IsNullOrEmpty(Path.GetDirectoryName(outputPath)))
        {
            return outputPath;
        }

        return Path.Combine("agent-output", outputPath);
    }

    private static IReadOnlyList<TimeSpan> ResolveSampleTimes(Scene scene, double[]? timeSeconds, int sampleCount)
    {
        if (timeSeconds is { Length: > 0 })
        {
            TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
            TimeSpan[] explicitTimes = timeSeconds
                .Select(seconds => double.IsFinite(seconds) ? Math.Max(0, seconds) : 0)
                .Select(seconds => TimeSpan.FromSeconds(Math.Min(seconds, duration.TotalSeconds)))
                .Distinct()
                .OrderBy(time => time)
                .ToArray();
            if (explicitTimes.Length >= 2)
            {
                return explicitTimes;
            }

            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "evaluate_motion_variation requires at least two distinct sample times."));
        }

        int count = Math.Clamp(sampleCount, 2, 8);
        double durationSeconds = scene.Duration > TimeSpan.Zero
            ? scene.Duration.TotalSeconds
            : 1;
        return Enumerable
            .Range(0, count)
            .Select(index => TimeSpan.FromSeconds(durationSeconds * (index + 0.5) / count))
            .ToArray();
    }

    private Scene RequireScene()
    {
        IEditingSession session = sessions.RequireSession();
        if (session.Root is Scene scene)
        {
            return scene;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "The current editing session is not attached to a scene."));
    }
}

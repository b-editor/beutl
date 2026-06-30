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
    [Description("Reviews the current scene for deterministic AI-editing quality risks: all-caps typography, visual hierarchy overload, short text read time, RectShape overuse, ambiguous decorative light shapes, hard gradient falloff, unclear foreground shapes, missing motion intent, invalid multi-object Element structure, high-tempo rhythm density/gaps, text backing alignment, palette problems, arbitrary dense effect stacks, dated card/shadow styling, low motion continuity, and chopped-up cut rhythm. Run after render_still and evaluate_motion_variation; resolve critical/major issues before export_video.")]
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

    [McpServerTool(Name = "preview_quality_risks")]
    [Description("Runs document-only deterministic quality risk checks without rendering. Use before or immediately after authoring a large apply_edit patch to catch text-density, RectShape, ambiguous decorative light shapes, hard gradient falloff, unclear-shape, missing-motion-intent, multi-object Element, high-tempo rhythm/gaps, backing-plate, palette, effect-stack, and timeline-structure risks early.")]
    public ValueTask<ToolResult<QualityReviewResponse>> PreviewQualityRisks(
        [Description("Optional profile label recorded in the review notes, such as kinetic-type, high-tempo-promo, editorial, or minimal.")]
        string? styleProfile = "preview-risks",
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
            return await qualityAnalyzer.AnalyzeAsync(
                scene,
                timeSeconds: null,
                sampleCount: 2,
                renderScale: 1,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                evaluateMotion: false,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "suggest_quality_fixes")]
    [Description("Groups current quality issues into minimal, patch-oriented fix suggestions. Use after preview_quality_risks or evaluate_edit_quality when an agent needs the smallest repair plan instead of raw issue rows.")]
    public ValueTask<ToolResult<QualityFixSuggestionsResponse>> SuggestQualityFixes(
        [Description("When true, include rendered motion checks; otherwise the tool stays document-only and faster.")]
        bool includeMotion = false,
        [Description("Optional explicit scene times in seconds for rendered motion checks when includeMotion is true.")]
        double[]? timeSeconds = null,
        [Description("Number of evenly spaced samples when timeSeconds is omitted and includeMotion is true. Clamped to 2..8.")]
        int sampleCount = 5,
        [Description("Supersampling render scale for rendered motion checks. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Optional profile label such as kinetic-type or high-tempo-promo.")]
        string? styleProfile = null,
        [Description("When true, an animatedPropertyCount of 0 is returned as a fix suggestion even if the quality gate otherwise passes.")]
        bool requireAnimatedProperties = false,
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
            IReadOnlyList<TimeSpan>? sampleTimes = includeMotion
                ? ResolveSampleTimes(scene, timeSeconds, sampleCount)
                : null;
            QualityReviewResponse review = await qualityAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                sampleCount,
                renderScale,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                includeMotion,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<QualityFixSuggestion> suggestions = BuildFixSuggestions(review, requireAnimatedProperties);
            bool passes = review.PassesQualityGate
                          && (!requireAnimatedProperties || review.Metrics.MotionContinuity.AnimatedPropertyCount > 0);
            string verdict = passes ? "no-quality-fixes-needed" : "quality-fixes-suggested";
            return new QualityFixSuggestionsResponse(
                passes,
                verdict,
                suggestions,
                review.Metrics,
                review.ReviewNotes);
        });
    }

    [McpServerTool(Name = "final_preflight")]
    [Description("Runs the normal final verification bundle before export_video: representative render_still files, evaluate_motion_variation, and evaluate_edit_quality. Returns ReadyForExport plus blockers and still paths.")]
    public ValueTask<ToolResult<FinalPreflightResponse>> FinalPreflight(
        [Description("Output prefix for still frames. Bare prefixes are written under agent-output/.")]
        string outputPrefix = "preflight",
        [Description("Optional explicit scene times in seconds. When omitted, samples are evenly spaced across the scene duration.")]
        double[]? timeSeconds = null,
        [Description("Number of evenly spaced samples when timeSeconds is omitted. Clamped to 2..8.")]
        int sampleCount = 5,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Optional profile label recorded in quality notes, such as kinetic-type, high-tempo-promo, editorial, or minimal.")]
        string? styleProfile = null,
        [Description("When true, animatedPropertyCount=0 blocks ReadyForExport even if rendered motion changed.")]
        bool requireAnimatedProperties = false,
        [Description("When true, long all-caps text is downgraded from major to minor instead of blocking the quality gate.")]
        bool allowAllCaps = false,
        [Description("When true, repeated hard-cut-like timing boundaries are not treated as major quality issues.")]
        bool allowHardCuts = false,
        [Description("When true, non-background RectShape dominance is not treated as a major quality issue.")]
        bool allowRectDominance = false,
        [Description("Required when a generated still output path already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            Scene scene = RequireScene();
            IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
            var stills = new List<PreflightStillFrame>(sampleTimes.Count);
            string normalizedPrefix = NormalizeOutputPath(outputPrefix);

            for (int i = 0; i < sampleTimes.Count; i++)
            {
                TimeSpan time = sampleTimes[i];
                string stillPath = CreatePreflightStillPath(normalizedPrefix, i, time);
                string resolvedPath = workspace.ResolveForWrite(stillPath);
                destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
                RenderStillResponse still = await stillRenderer.RenderAsync(
                    scene,
                    time,
                    resolvedPath,
                    renderScale,
                    cancellationToken).ConfigureAwait(false);
                stills.Add(new PreflightStillFrame(
                    still.OutputPath,
                    still.Time,
                    still.Warnings,
                    still.VisibilityAnalysis,
                    still.ActiveElements));
            }

            MotionVariationResponse motion = await motionVariationAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                renderScale,
                0.02,
                48,
                0.35,
                0.90,
                24,
                cancellationToken).ConfigureAwait(false);
            QualityReviewResponse quality = await qualityAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                sampleTimes.Count,
                renderScale,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                evaluateMotion: true,
                cancellationToken).ConfigureAwait(false);

            List<string> blockers = [];
            if (!motion.PassesMinimumMotion)
            {
                blockers.Add($"Motion variation did not pass: {motion.Verdict}.");
            }

            if (!quality.PassesQualityGate)
            {
                blockers.Add("evaluate_edit_quality reported critical or major issues.");
            }

            if (requireAnimatedProperties && quality.Metrics.MotionContinuity.AnimatedPropertyCount == 0)
            {
                blockers.Add("animatedPropertyCount is 0; add explicit transform, opacity, brush, effect, or typography animation before exporting motion graphics.");
            }

            if (stills.Any(item => item.Warnings.Count > 0))
            {
                blockers.Add("One or more representative still renders returned visibility warnings.");
            }

            bool ready = blockers.Count == 0;
            return new FinalPreflightResponse(
                ready,
                blockers,
                stills,
                motion,
                quality,
                ready ? "export_video" : "suggest_quality_fixes");
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

    private static string CreatePreflightStillPath(string normalizedPrefix, int index, TimeSpan time)
    {
        string directory = Path.GetDirectoryName(normalizedPrefix) ?? string.Empty;
        string filePrefix = Path.GetFileNameWithoutExtension(normalizedPrefix);
        if (string.IsNullOrWhiteSpace(filePrefix))
        {
            filePrefix = "preflight";
        }

        string extension = Path.GetExtension(normalizedPrefix);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        long milliseconds = Math.Max(0, (long)Math.Round(time.TotalMilliseconds));
        string fileName = $"{filePrefix}-still-{index:D2}-{milliseconds:D8}ms{extension}";
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : Path.Combine(directory, fileName);
    }

    private static IReadOnlyList<QualityFixSuggestion> BuildFixSuggestions(
        QualityReviewResponse review,
        bool requireAnimatedProperties)
    {
        var suggestions = review.Issues
            .GroupBy(issue => issue.Category, StringComparer.Ordinal)
            .Select(group =>
            {
                QualityIssue[] issues = group.ToArray();
                string severity = issues
                    .Select(issue => issue.Severity)
                    .OrderBy(SeverityRank)
                    .First();
                return new QualityFixSuggestion(
                    group.Key,
                    severity,
                    issues.Length,
                    issues[0].Message,
                    CreateFixStrategy(group.Key, issues[0].SuggestedFix),
                    issues.SelectMany(issue => issue.ElementIds).Distinct(StringComparer.Ordinal).ToArray(),
                    issues.SelectMany(issue => issue.ObjectIds).Distinct(StringComparer.Ordinal).ToArray());
            })
            .ToList();

        if (requireAnimatedProperties
            && review.Metrics.MotionContinuity.AnimatedPropertyCount == 0)
        {
            var animatedPropertiesFix = new QualityFixSuggestion(
                "motionContinuity",
                "major",
                1,
                "Motion graphics require explicit animated properties.",
                "Add keyframed transform, opacity, brush, effect, or typography spacing to at least one visible object before export.",
                [],
                []);
            int existingMotionSuggestion = suggestions.FindIndex(item => item.Category == "motionContinuity");
            if (existingMotionSuggestion >= 0)
            {
                QualityFixSuggestion existing = suggestions[existingMotionSuggestion];
                suggestions[existingMotionSuggestion] = existing with
                {
                    Severity = SeverityRank(existing.Severity) > SeverityRank(animatedPropertiesFix.Severity)
                        ? animatedPropertiesFix.Severity
                        : existing.Severity,
                    MinimalPatchStrategy = animatedPropertiesFix.MinimalPatchStrategy
                };
            }
            else
            {
                suggestions.Add(animatedPropertiesFix);
            }
        }

        return suggestions;
    }

    private static int SeverityRank(string severity)
    {
        return severity switch
        {
            "critical" => 0,
            "major" => 1,
            "minor" => 2,
            _ => 3
        };
    }

    private static string CreateFixStrategy(string category, string fallback)
    {
        return category switch
        {
            "typographyReadTime" => "For 1.5s beats, replace sentences with 1-3 word hero text and 2-4 word supporting tokens, or split copy into separate Elements.",
            "shapeDiversity" => "Delete or replace foreground RectShape accents with EllipseShape, RoundedRectShape, GeometryShape, strokes, media, or procedural texture; keep RectShape for full-frame [role:background] surfaces.",
            "decorativeShapeClarity" => "Replace abstract glint/glow/aperture ellipses with concrete, parseable visual systems such as strokes, particles, letter fragments, editor/timeline marks, masks, media, or procedural texture; move pure atmosphere to [role:background].",
            "gradientFalloff" => "For ambient/aperture/glow gradients, use at least three falloff stops, widen abrupt stop offsets, add Blur/SKSL texture when appropriate, or replace the shape with procedural surface texture.",
            "shapeIntent" => "Rename/tag each large or animated foreground shape with a clear role and purpose, or remove it if it does not serve the shot.",
            "motionIntent" => "Rename/tag animated foreground shapes with a concrete motion job such as beat slide, scan sweep, pulse reveal, wipe transition, or impact burst.",
            "elementStructure" => "Split ordinary content into one Element per EngineObject; keep multi-object Elements only for IFlowOperator chains such as DrawableGroup, DrawableDecorator, SoundGroup, or Scene3D.",
            "tempoRhythm" => "Convert the BPM target into a beat grid, add visible foreground boundaries every 1-2 beats, close long foreground event gaps, and keep normal foreground holds near 2-4 beats.",
            "textBackgroundFit" => "Use an explicit [role:text-backing] shape only for real text plates, match its Start/Length to the text Element, then verify with measure_object_bounds.",
            "visualHierarchy" => "Keep one hero-scale text object per beat and reduce supporting labels below hero size and contrast.",
            "effectIntent" => "Remove repeated decorative effect stacks or rename and keep only chains with one job: texture, hierarchy, transition energy, grade, or legibility.",
            "motionContinuity" => "Add bridged opacity/transform/spacing/brush/effect keyframes across cut boundaries and keep animatedPropertyCount above zero for motion graphics.",
            "cutRhythm" => "Bridge adjacent Elements with short overlaps, opacity fades, or transform continuation instead of pure hard boundaries.",
            _ => fallback
        };
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

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Design;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensions.FFmpeg;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PaletteRoleColor = Beutl.AgentToolkit.Rendering.PaletteRoleColor;

namespace Beutl.AgentToolkit.Tools;

public sealed record AnalyzeAudioRhythmResponse(
    string SchemaVersion,
    int SampleRate,
    AudioRhythmWindow AnalyzedWindow,
    double EstimatedBpm,
    double Confidence,
    IReadOnlyList<double> BeatTimesSeconds,
    IReadOnlyList<double> StrongOnsetTimesSeconds,
    IReadOnlyList<string> Guidance);

[McpServerToolType]
public sealed class RenderTools(
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard,
    StillRenderer stillRenderer,
    StoryboardRenderer storyboardRenderer,
    MotionVariationAnalyzer motionVariationAnalyzer,
    AudioRhythmAnalyzer audioRhythmAnalyzer,
    QualityAnalyzer qualityAnalyzer,
    VideoExporter videoExporter,
    RenderJobManager renderJobs) : ToolBase
{
    private static readonly JsonSerializerOptions s_jobResultOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);
    private static readonly string s_processOutputToken = CreateProcessOutputToken();
    private const int DefaultSceneFrameRate = 30;
    private const int MaxStoryboardFrameCount = 48;
    private const int MaxStoryboardSubdivisionLevel = 3;
    private const string StoryboardFrameKindShot = "shot";
    private const string StoryboardFrameKindInbetween = "inbetween";

    [McpServerTool(Name = "render_still")]
    [Description("Renders a still PNG from the current scene to a workspace-relative output path and returns visibility warnings for blank or near-black frames. Bare filenames are written under agent-output/. By default the tool returns the same JSON text payload as before; pass returnImageContent:true to append a downscaled image/png content block for multimodal review.")]
    public ValueTask<CallToolResult> RenderStill(
        [Description("Workspace-relative or in-workspace absolute output path. Bare filenames are written under agent-output/. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Scene time in seconds. Use this exact parameter name; time is not a render_still parameter.")]
        double timeSeconds = 0,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        [Description("When true, append a downscaled image/png MCP content block with a long edge of about 768 px. Default false preserves the path-only JSON response.")]
        bool returnImageContent = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMcpAsync<RenderStillResponse>(async () =>
        {
            Scene scene = RequireSceneSnapshot();
            string resolvedPath = workspace.ResolveForWrite(NormalizeOutputPath(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            RenderStillResponse response = await stillRenderer.RenderAsync(
                scene,
                TimeSpan.FromSeconds(Math.Max(0, timeSeconds)),
                resolvedPath,
                renderScale,
                cancellationToken).ConfigureAwait(false);
            ImageContentBlock? image = returnImageContent
                ? ImageContentBlock.FromBytes(
                    ImagePreviewEncoder.EncodePngFile(response.OutputPath),
                    "image/png")
                : null;
            return (response, image);
        });
    }

    [McpServerTool(Name = "render_storyboard")]
    [Description("Renders a storyboard contact sheet from explicit sample times, explicit shots, or one auto-derived midpoint per timeline Element. Writes individual still PNGs and the contact sheet inside BEUTL_WORKSPACE. Pass timeSeconds for continuous single-shot pieces where Element-boundary shot detection would collapse the arc. Pass subdivisionLevel:1..3 to insert binary in-between frames between adjacent anchors for transition review. For scenes with many Elements this can exceed the MCP client request timeout; pass background:true to run it as a job and poll read_render_job(jobId) instead. By default the tool returns the same JSON text payload as before; pass returnImageContent:true on a synchronous call to append one downscaled image/png contact-sheet content block.")]
    public ValueTask<CallToolResult> RenderStoryboard(
        [Description("Optional explicit storyboard shots. When omitted, one midpoint is derived per timeline Element.")]
        StoryboardShotInput[]? shots = null,
        [Description("Optional explicit scene times in seconds. When supplied, this overrides shots and auto shot detection entirely; each value becomes an anchor frame named t:<seconds>. Values must be finite, within the scene duration, non-empty, and stay within the 48-frame cap after subdivision.")]
        double[]? timeSeconds = null,
        [Description("Workspace-relative or in-workspace absolute output directory. Existing files require confirmOverwrite.")]
        string outputDirectory = "agent-output",
        [Description("Basename used for generated still PNGs and the contact sheet. Omit for a collision-free default containing the active session id; explicit values preserve exact filenames.")]
        string? basename = null,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when generated output paths already exist.")]
        bool confirmOverwrite = false,
        [Description("When true, run the render as a background job and return {status:running, jobId} immediately; poll read_render_job(jobId) for completion. Do not issue apply_edit while a background render is running.")]
        bool background = false,
        [Description("When true on a synchronous call, append a downscaled image/png MCP content block of the storyboard contact sheet. Cannot be combined with background:true because the image is not available until the job completes.")]
        bool returnImageContent = false,
        [Description("Binary subdivision depth for in-between frames between adjacent shots. 0 keeps the current one-frame-per-shot behavior; 1 adds midpoints, 2 adds quarter points, 3 adds eighth points. Values are clamped to 0..3.")]
        int subdivisionLevel = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMcpAsync<RenderStoryboardResult>(async () =>
        {
            if (background && returnImageContent)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "returnImageContent cannot be combined with background:true; run render_storyboard synchronously when the contact-sheet image block is needed."));
            }

            Scene scene = RequireSceneSnapshot(forceClone: background);
            int normalizedSubdivisionLevel = NormalizeStoryboardSubdivisionLevel(subdivisionLevel);
            IReadOnlyList<ResolvedStoryboardFrame> resolvedShots = ResolveStoryboardFrames(
                scene,
                shots,
                timeSeconds,
                normalizedSubdivisionLevel);
            ValidateStoryboardFrameCount(
                resolvedShots.Count,
                normalizedSubdivisionLevel,
                timeSeconds is null ? "subdivisionLevel" : "timeSeconds");
            string normalizedDirectory = NormalizeStoryboardDirectory(outputDirectory);
            string safeBasename = NormalizeStoryboardBasename(basename ?? CreateDefaultOutputBasename("storyboard"));

            var plannedShots = new List<(ResolvedStoryboardFrame Shot, string ResolvedPath)>(resolvedShots.Count);
            for (int i = 0; i < resolvedShots.Count; i++)
            {
                ResolvedStoryboardFrame shot = resolvedShots[i];
                string stillPath = Path.Combine(
                    normalizedDirectory,
                    $"{safeBasename}-shot-{i:D2}-{Math.Max(0, (long)Math.Round(shot.Time.TotalMilliseconds)):D8}ms.png");
                string resolvedPath = workspace.ResolveForWrite(stillPath);
                destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
                plannedShots.Add((shot, resolvedPath));
            }

            string contactSheetPath = Path.Combine(normalizedDirectory, $"{safeBasename}-contact-sheet.png");
            string resolvedContactSheetPath = workspace.ResolveForWrite(contactSheetPath);
            destructiveGuard.EnsureOverwriteAllowed(resolvedContactSheetPath, confirmOverwrite);

            async Task<RenderStoryboardResponse> RunStoryboardAsync(CancellationToken token)
            {
                // Re-verify the overwrite guards at write time: background jobs are serialized, so a
                // preceding job may have created these files after the pre-flight check passed.
                foreach ((_, string plannedPath) in plannedShots)
                {
                    destructiveGuard.EnsureOverwriteAllowed(plannedPath, confirmOverwrite);
                }

                destructiveGuard.EnsureOverwriteAllowed(resolvedContactSheetPath, confirmOverwrite);

                var renderedShots = new List<RenderStoryboardShot>(plannedShots.Count);
                var contactSheetFrames = new List<StoryboardContactSheetFrame>(plannedShots.Count);
                var eyeTraceFrames = new List<StoryboardEyeTraceFrame>(plannedShots.Count);
                foreach ((ResolvedStoryboardFrame shot, string resolvedPath) in plannedShots)
                {
                    using RenderedFrameAnalysis frame = await stillRenderer.RenderFrameAnalysisAsync(
                        scene,
                        shot.Time,
                        renderScale,
                        token).ConfigureAwait(false);
                    SaveStoryboardStill(frame.Bitmap, resolvedPath);
                    StillFrameVisibilityAnalysis visibility = StillRenderer.AnalyzeFrameVisibility(frame.Bitmap);
                    NormalizedFocalPoint? focalPoint = string.Equals(shot.Kind, StoryboardFrameKindShot, StringComparison.Ordinal)
                        ? StillRenderer.EstimateFocalPoint(scene, frame, visibility)
                        : null;
                    renderedShots.Add(new RenderStoryboardShot(
                        shot.Name,
                        shot.Time.TotalSeconds,
                        resolvedPath,
                        visibility,
                        shot.Kind,
                        shot.SubdivisionLevel));
                    contactSheetFrames.Add(new StoryboardContactSheetFrame(
                        shot.Name,
                        shot.Time.TotalSeconds,
                        resolvedPath,
                        shot.Kind,
                        shot.SubdivisionLevel));
                    if (focalPoint is not null)
                    {
                        eyeTraceFrames.Add(new StoryboardEyeTraceFrame(
                            shot.Name,
                            focalPoint));
                    }
                }

                storyboardRenderer.RenderContactSheet(contactSheetFrames, resolvedContactSheetPath);
                CutEyeTrace[] cutEyeTrace = BuildCutEyeTrace(eyeTraceFrames);
                List<string> reviewNotes = [];
                if (cutEyeTrace.Any(item => item.ExceedsEyeTraceBudget))
                {
                    reviewNotes.Add("One or more adjacent anchor shots exceed the eye-trace budget. Murch's eye-trace criterion suggests adding a bridging element, sweep, or focal-point realignment so the viewer's attention lands near the next shot's point of interest.");
                }

                return new RenderStoryboardResponse(resolvedContactSheetPath, renderedShots, cutEyeTrace, reviewNotes);
            }

            if (background)
            {
                string jobId = renderJobs.Enqueue(
                    "storyboard",
                    async token => JsonSerializer.SerializeToNode(
                        await RunStoryboardAsync(token).ConfigureAwait(false),
                        s_jobResultOptions)!);
                return (new RenderStoryboardResult("running", jobId, null), (ImageContentBlock?)null);
            }

            RenderStoryboardResponse response = await RunStoryboardAsync(cancellationToken).ConfigureAwait(false);
            ImageContentBlock? image = returnImageContent
                ? ImageContentBlock.FromBytes(
                    storyboardRenderer.RenderContactSheetPng(
                        response.Shots
                            .Select(shot => new StoryboardContactSheetFrame(
                                shot.Name,
                                shot.TimeSeconds,
                                shot.StillPath,
                                shot.Kind,
                                shot.SubdivisionLevel))
                            .ToArray(),
                        ImagePreviewEncoder.DefaultMaxLongEdge).Bytes,
                    "image/png")
                : null;
            return (new RenderStoryboardResult("completed", null, response), image);
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
            Scene scene = RequireSceneSnapshot();
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

    [McpServerTool(Name = "analyze_audio_rhythm")]
    [Description("Decodes an audio/music-bed file through Beutl's audio source path and returns measured BPM, beat times, and strong onset times for timeline planning. Reads are unrestricted; nonexistent paths return media_not_found. Use beatTimesSeconds to anchor Element boundaries/accent keyframes and pass the same array to evaluate_edit_quality/final_preflight for audioSync advisories.")]
    public ValueTask<ToolResult<AnalyzeAudioRhythmResponse>> AnalyzeAudioRhythm(
        [Description("Readable audio file path. Relative paths are resolved against the current process directory; reads are not workspace-guarded.")]
        string path,
        [Description("Optional start time in seconds for the analysis window. Defaults to 0.")]
        double? startSeconds = null,
        [Description("Optional positive duration in seconds for the analysis window. Defaults to the remaining decoded audio.")]
        double? durationSeconds = null,
        [Description("Optional lower BPM constraint. The estimator always stays inside the supported 60-200 BPM range.")]
        double? expectedBpmMin = null,
        [Description("Optional upper BPM constraint. The estimator always stays inside the supported 60-200 BPM range.")]
        double? expectedBpmMax = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            AudioRhythmAnalysis analysis = await audioRhythmAnalyzer.AnalyzeFileAsync(
                path,
                startSeconds,
                durationSeconds,
                expectedBpmMin,
                expectedBpmMax,
                cancellationToken).ConfigureAwait(false);

            return new AnalyzeAudioRhythmResponse(
                SchemaVersion.Current,
                analysis.SampleRate,
                analysis.AnalyzedWindow,
                analysis.EstimatedBpm,
                analysis.Confidence,
                analysis.BeatTimesSeconds,
                analysis.StrongOnsetTimesSeconds,
                [
                    "Anchor visible Element start/end boundaries and accent keyframes to beatTimesSeconds when the edit should fuse with the music bed.",
                    "Use strongOnsetTimesSeconds for downbeats, impacts, lyric starts, or other accent events.",
                    "Pass beatTimesSeconds unchanged to evaluate_edit_quality or final_preflight so audioSync can flag cuts that are 40-120 ms off-grid."
                ]);
        });
    }

    [McpServerTool(Name = "evaluate_edit_quality")]
    [Description("Reviews the current scene for deterministic AI-editing quality risks: all-caps typography, visual hierarchy overload, short text read time, rendered text contrast, RectShape overuse, ambiguous decorative light shapes, hard gradient falloff, flat background richness, unclear foreground shapes, missing motion intent, invalid multi-object Element structure, layer density/depth coverage, high-tempo rhythm density/gaps, audio beat sync, text backing alignment, palette harmony, arbitrary dense effect stacks, dated card/shadow styling, all-linear easing monotony, uniform motion clusters, logo-intro motion-arc gaps, low motion continuity, chopped-up cut rhythm, and video-type timeline coverage. Run after render_still and evaluate_motion_variation; resolve critical/major issues before export_video. Gate-failing checks are limited to the typography blocker family (short read time and rendered text contrast), multi-object Element structure, low motion continuity, and motion-graphics layer density below half of a supplied quantitative plan. A deliberate, brief-justified deviation is allowed and downgraded to advisory via its intent flag (allowStillness, allowDenseText, allowMultiObjectElements, allowMonochrome, allowMinimalDensity), videoType profile, or an equivalent [role:...] tag on the element, so the gate blocks likely accidents, not intentional creative choices.")]
    public ValueTask<ToolResult<QualityReviewResponse>> EvaluateEditQuality(
        [Description("Optional video workflow profile. Supported values: motion-graphics, footage-cut, slideshow, lyric-captions, logo-intro. Omit for exactly the legacy motion-graphics behavior.")]
        string? videoType = null,
        [Description("Optional explicit scene times in seconds for rendered motion checks. When omitted, samples evenly across the scene duration.")]
        double[]? timeSeconds = null,
        [Description("Number of evenly spaced samples when timeSeconds is omitted. Clamped to 2..8.")]
        int sampleCount = 5,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Optional profile label recorded in the review notes, such as draft, editorial, kinetic-type, or minimal.")]
        string? styleProfile = null,
        [Description("When true, tailors the intentional all-caps suggested fix. All-caps typography is already advisory (it never blocks the gate); this flag adjusts guidance, not severity.")]
        bool allowAllCaps = false,
        [Description("When true, suppresses the repeated hard-cut cadence advisory. Hard cuts are already advisory (they never block the gate); this flag removes the note.")]
        bool allowHardCuts = false,
        [Description("When true, suppresses the non-background RectShape-dominance advisory. Rect dominance is already advisory (it never blocks the gate).")]
        bool allowRectDominance = false,
        [Description("When true, relax the non-blocking aesthetic and pacing advisories in one switch (ambiguous decorative shapes, Material-UI card look, RectShape dominance, hard cuts, and high-tempo long-hold/short-segment pacing), so expressive shape-rich or kinetic-typography briefs are not nudged toward a plainer look. Blocking checks (read time, element structure, motion, and supplied-plan density violation) are unaffected.")]
        bool relaxAesthetics = false,
        [Description("When true, deliberate stillness / held-frame / negative-space composition is allowed: the low motion-continuity blocker is downgraded from major to advisory instead of failing the gate. Tagging an element/object [role:still] (or naming it 'hold frame', 'negative space', etc.) opts in the same way without this flag.")]
        bool allowStillness = false,
        [Description("When true, brief-justified dense or long copy on a short-lived text element is allowed: the read-time blocker is downgraded from major to advisory. Tagging the text [role:reading]/[role:manifesto]/[role:credits] (or so naming it) opts in the same way without this flag.")]
        bool allowDenseText = false,
        [Description("When true, an intentional composite Element holding multiple EngineObjects without an IFlowOperator is allowed: the element-structure blocker is downgraded from major to advisory. Tagging the Element [role:composite] opts in the same way without this flag.")]
        bool allowMultiObjectElements = false,
        [Description("When true, an intentional monochrome / low-contrast palette is allowed: the low luma-separation advisory is suppressed. Tagging an element/object [role:monochrome]/[role:low-contrast] (or so naming it) opts in the same way without this flag.")]
        bool allowMonochrome = false,
        [Description("When true, deliberate minimal / sparse / negative-space density is allowed: a motion-graphics density plan violation is downgraded from major to advisory. Tagging an element/object [role:minimal] or [role:negative-space] opts in the same way without this flag.")]
        bool allowMinimalDensity = false,
        [Description("Optional quantitativePlanSheet target for planned foreground elements per shot. When > 0 and a motion-graphics scene authors fewer than half this foreground layer count in any measured time band, layerDensity can fail the gate unless allowMinimalDensity or a minimal role tag is present.")]
        double plannedForegroundElementsPerShot = 0,
        [Description("Optional beat grid from analyze_audio_rhythm. When supplied, audioSync reports advisory-only visible cut boundaries 40-120 ms from the nearest beat.")]
        double[]? beatTimesSeconds = null,
        [Description("Optional colors from derive_palette as a JSON array of role/color pairs or a JSON string containing that array, for rendered 60-30-10 palette-balance metrics. Each item is { role: string, color: \"#RRGGBB\" }. Skipped for footage-cut.")]
        JsonElement? paletteRoleColors = null,
        [Description("When true, treats the scene as a static layout and skips rendered motion checks.")]
        bool staticLayout = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            ValidateVideoType(videoType);
            Scene scene = RequireSceneSnapshot();
            // Capture the session key alongside the snapshot: a switch during the render must not file
            // the baseline under another project's key.
            string sessionKey = sessions.CurrentSessionKey;
            PaletteRoleColor[]? parsedPaletteRoleColors = ParsePaletteRoleColors(paletteRoleColors);
            IReadOnlyList<TimeSpan>? sampleTimes = staticLayout
                ? NormalizeQualitySampleTimesOrNull(timeSeconds)
                : ResolveQualitySampleTimes(scene, timeSeconds, sampleCount);
            QualityAnalysisOptions options = CreateQualityAnalysisOptions(
                videoType,
                styleProfile,
                renderScale,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                relaxAesthetics,
                allowStillness,
                allowDenseText,
                allowMultiObjectElements,
                allowMonochrome,
                allowMinimalDensity,
                plannedForegroundElementsPerShot,
                beatTimesSeconds,
                parsedPaletteRoleColors);
            QualityReviewResponse review = await qualityAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                sampleCount,
                options.RenderScale,
                options.StyleProfile,
                options.AllowAllCaps,
                options.AllowHardCuts,
                options.AllowRectDominance,
                options.RelaxAesthetics,
                options.AllowStillness,
                options.AllowDenseText,
                options.AllowMultiObjectElements,
                options.AllowMonochrome,
                options.AllowMinimalDensity,
                options.PlannedForegroundElementsPerShot,
                evaluateMotion: !staticLayout,
                videoType: options.VideoType,
                beatTimesSeconds: options.BeatTimesSeconds,
                paletteRoleColors: options.PaletteRoleColors,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!staticLayout && sampleTimes is not null)
            {
                IReadOnlyList<string> stillPaths = await RenderBaselineStillsAsync(
                    scene,
                    sampleTimes,
                    options.RenderScale,
                    "quality-baseline",
                    cancellationToken).ConfigureAwait(false);
                StoreQualityBaseline(sessionKey, sampleTimes, options, review, stillPaths);
            }

            return review;
        });
    }

    [McpServerTool(Name = "preview_quality_risks")]
    [Description("Runs document-only deterministic quality risk checks without rendering. Use before or immediately after authoring a large apply_edit patch to catch text-density, RectShape, ambiguous decorative light shapes, hard gradient falloff, flat background richness, unclear-shape, missing-motion-intent, layer-density/depth, multi-object Element, high-tempo rhythm/gaps, backing-plate, palette, effect-stack, easing/motion-uniformity, logo-intro motion-arc, video-type timeline coverage, and timeline-structure risks early.")]
    public ValueTask<ToolResult<QualityReviewResponse>> PreviewQualityRisks(
        [Description("Optional video workflow profile. Supported values: motion-graphics, footage-cut, slideshow, lyric-captions, logo-intro. Omit for exactly the legacy motion-graphics behavior.")]
        string? videoType = null,
        [Description("Optional profile label recorded in the review notes, such as kinetic-type, high-tempo-promo, editorial, or minimal.")]
        string? styleProfile = "preview-risks",
        [Description("When true, tailors the intentional all-caps suggested fix. All-caps typography is already advisory (it never blocks the gate); this flag adjusts guidance, not severity.")]
        bool allowAllCaps = false,
        [Description("When true, suppresses the repeated hard-cut cadence advisory. Hard cuts are already advisory (they never block the gate); this flag removes the note.")]
        bool allowHardCuts = false,
        [Description("When true, suppresses the non-background RectShape-dominance advisory. Rect dominance is already advisory (it never blocks the gate).")]
        bool allowRectDominance = false,
        [Description("When true, relax the non-blocking aesthetic and pacing advisories in one switch (ambiguous decorative shapes, Material-UI card look, RectShape dominance, hard cuts, and high-tempo long-hold/short-segment pacing), so expressive shape-rich or kinetic-typography briefs are not nudged toward a plainer look. Blocking checks (read time, element structure, motion, and supplied-plan density violation) are unaffected.")]
        bool relaxAesthetics = false,
        [Description("When true, deliberate stillness / held-frame / negative-space composition is allowed: the low motion-continuity blocker is downgraded from major to advisory instead of failing the gate. Tagging an element/object [role:still] (or naming it 'hold frame', 'negative space', etc.) opts in the same way without this flag.")]
        bool allowStillness = false,
        [Description("When true, brief-justified dense or long copy on a short-lived text element is allowed: the read-time blocker is downgraded from major to advisory. Tagging the text [role:reading]/[role:manifesto]/[role:credits] (or so naming it) opts in the same way without this flag.")]
        bool allowDenseText = false,
        [Description("When true, an intentional composite Element holding multiple EngineObjects without an IFlowOperator is allowed: the element-structure blocker is downgraded from major to advisory. Tagging the Element [role:composite] opts in the same way without this flag.")]
        bool allowMultiObjectElements = false,
        [Description("When true, an intentional monochrome / low-contrast palette is allowed: the low luma-separation advisory is suppressed. Tagging an element/object [role:monochrome]/[role:low-contrast] (or so naming it) opts in the same way without this flag.")]
        bool allowMonochrome = false,
        [Description("When true, deliberate minimal / sparse / negative-space density is allowed: a motion-graphics density plan violation is downgraded from major to advisory. Tagging an element/object [role:minimal] or [role:negative-space] opts in the same way without this flag.")]
        bool allowMinimalDensity = false,
        [Description("Optional quantitativePlanSheet target for planned foreground elements per shot. When > 0 and a motion-graphics scene authors fewer than half this foreground layer count in any measured time band, layerDensity can fail the gate unless allowMinimalDensity or a minimal role tag is present.")]
        double plannedForegroundElementsPerShot = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            ValidateVideoType(videoType);
            Scene scene = RequireSceneSnapshot();
            return await qualityAnalyzer.AnalyzeAsync(
                scene,
                timeSeconds: null,
                sampleCount: 2,
                renderScale: 1,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                relaxAesthetics,
                allowStillness,
                allowDenseText,
                allowMultiObjectElements,
                allowMonochrome,
                allowMinimalDensity,
                plannedForegroundElementsPerShot,
                evaluateMotion: false,
                videoType: videoType,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "suggest_quality_fixes")]
    [Description("Groups current quality issues into minimal, patch-oriented fix suggestions. Use after preview_quality_risks or evaluate_edit_quality when an agent needs the smallest repair plan instead of raw issue rows.")]
    public ValueTask<ToolResult<QualityFixSuggestionsResponse>> SuggestQualityFixes(
        [Description("Optional video workflow profile. Supported values: motion-graphics, footage-cut, slideshow, lyric-captions, logo-intro. Omit for exactly the legacy motion-graphics behavior.")]
        string? videoType = null,
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
        [Description("When true, tailors the intentional all-caps suggested fix. All-caps typography is already advisory (it never blocks the gate); this flag adjusts guidance, not severity.")]
        bool allowAllCaps = false,
        [Description("When true, suppresses the repeated hard-cut cadence advisory. Hard cuts are already advisory (they never block the gate); this flag removes the note.")]
        bool allowHardCuts = false,
        [Description("When true, suppresses the non-background RectShape-dominance advisory. Rect dominance is already advisory (it never blocks the gate).")]
        bool allowRectDominance = false,
        [Description("When true, relax the non-blocking aesthetic and pacing advisories in one switch (ambiguous decorative shapes, Material-UI card look, RectShape dominance, hard cuts, and high-tempo long-hold/short-segment pacing), so expressive shape-rich or kinetic-typography briefs are not nudged toward a plainer look. Blocking checks (read time, element structure, motion, and supplied-plan density violation) are unaffected.")]
        bool relaxAesthetics = false,
        [Description("When true, deliberate stillness / held-frame / negative-space composition is allowed: the low motion-continuity blocker is downgraded from major to advisory instead of failing the gate. Tagging an element/object [role:still] (or naming it 'hold frame', 'negative space', etc.) opts in the same way without this flag.")]
        bool allowStillness = false,
        [Description("When true, brief-justified dense or long copy on a short-lived text element is allowed: the read-time blocker is downgraded from major to advisory. Tagging the text [role:reading]/[role:manifesto]/[role:credits] (or so naming it) opts in the same way without this flag.")]
        bool allowDenseText = false,
        [Description("When true, an intentional composite Element holding multiple EngineObjects without an IFlowOperator is allowed: the element-structure blocker is downgraded from major to advisory. Tagging the Element [role:composite] opts in the same way without this flag.")]
        bool allowMultiObjectElements = false,
        [Description("When true, an intentional monochrome / low-contrast palette is allowed: the low luma-separation advisory is suppressed. Tagging an element/object [role:monochrome]/[role:low-contrast] (or so naming it) opts in the same way without this flag.")]
        bool allowMonochrome = false,
        [Description("When true, deliberate minimal / sparse / negative-space density is allowed: a motion-graphics density plan violation is downgraded from major to advisory. Tagging an element/object [role:minimal] or [role:negative-space] opts in the same way without this flag.")]
        bool allowMinimalDensity = false,
        [Description("Optional quantitativePlanSheet target for planned foreground elements per shot. When > 0 and a motion-graphics scene authors fewer than half this foreground layer count in any measured time band, layerDensity can fail the gate unless allowMinimalDensity or a minimal role tag is present.")]
        double plannedForegroundElementsPerShot = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            ValidateVideoType(videoType);
            Scene scene = RequireSceneSnapshot();
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
                relaxAesthetics,
                allowStillness,
                allowDenseText,
                allowMultiObjectElements,
                allowMonochrome,
                allowMinimalDensity,
                plannedForegroundElementsPerShot,
                includeMotion,
                videoType: videoType,
                cancellationToken: cancellationToken).ConfigureAwait(false);

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
        [Description("Optional video workflow profile. Supported values: motion-graphics, footage-cut, slideshow, lyric-captions, logo-intro. Omit for exactly the legacy motion-graphics behavior.")]
        string? videoType = null,
        [Description("Output prefix for still frames. Bare prefixes are written under agent-output/. Omit for a collision-free default containing the active session id; explicit values preserve exact filenames.")]
        string? outputPrefix = null,
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
        [Description("When true, tailors the intentional all-caps suggested fix. All-caps typography is already advisory (it never blocks the gate); this flag adjusts guidance, not severity.")]
        bool allowAllCaps = false,
        [Description("When true, suppresses the repeated hard-cut cadence advisory. Hard cuts are already advisory (they never block the gate); this flag removes the note.")]
        bool allowHardCuts = false,
        [Description("When true, suppresses the non-background RectShape-dominance advisory. Rect dominance is already advisory (it never blocks the gate).")]
        bool allowRectDominance = false,
        [Description("When true, relax the non-blocking aesthetic and pacing advisories in one switch (ambiguous decorative shapes, Material-UI card look, RectShape dominance, hard cuts, and high-tempo long-hold/short-segment pacing), so expressive shape-rich or kinetic-typography briefs are not nudged toward a plainer look. Blocking checks (read time, element structure, motion, and supplied-plan density violation) are unaffected.")]
        bool relaxAesthetics = false,
        [Description("When true, deliberate stillness / held-frame / negative-space composition is allowed: the low motion-continuity blocker is downgraded from major to advisory instead of failing the gate. Tagging an element/object [role:still] (or naming it 'hold frame', 'negative space', etc.) opts in the same way without this flag.")]
        bool allowStillness = false,
        [Description("When true, brief-justified dense or long copy on a short-lived text element is allowed: the read-time blocker is downgraded from major to advisory. Tagging the text [role:reading]/[role:manifesto]/[role:credits] (or so naming it) opts in the same way without this flag.")]
        bool allowDenseText = false,
        [Description("When true, an intentional composite Element holding multiple EngineObjects without an IFlowOperator is allowed: the element-structure blocker is downgraded from major to advisory. Tagging the Element [role:composite] opts in the same way without this flag.")]
        bool allowMultiObjectElements = false,
        [Description("When true, an intentional monochrome / low-contrast palette is allowed: the low luma-separation advisory is suppressed. Tagging an element/object [role:monochrome]/[role:low-contrast] (or so naming it) opts in the same way without this flag.")]
        bool allowMonochrome = false,
        [Description("When true, deliberate minimal / sparse / negative-space density is allowed: a motion-graphics density plan violation is downgraded from major to advisory. Tagging an element/object [role:minimal] or [role:negative-space] opts in the same way without this flag.")]
        bool allowMinimalDensity = false,
        [Description("Optional quantitativePlanSheet target for planned foreground elements per shot. When > 0 and a motion-graphics scene authors fewer than half this foreground layer count in any measured time band, layerDensity can fail the gate unless allowMinimalDensity or a minimal role tag is present.")]
        double plannedForegroundElementsPerShot = 0,
        [Description("Optional beat grid from analyze_audio_rhythm. When supplied, audioSync reports advisory-only visible cut boundaries 40-120 ms from the nearest beat.")]
        double[]? beatTimesSeconds = null,
        [Description("Optional colors from derive_palette as a JSON array of role/color pairs or a JSON string containing that array, for rendered 60-30-10 palette-balance metrics. Each item is { role: string, color: \"#RRGGBB\" }. Skipped for footage-cut.")]
        JsonElement? paletteRoleColors = null,
        [Description("When true, treats the scene as a static storyboard layout and skips motion blockers.")]
        bool staticLayout = false,
        [Description("Required when a generated still output path already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            ValidateVideoType(videoType);
            Scene scene = RequireSceneSnapshot();
            // Capture the session key alongside the snapshot: a switch during the render must not file
            // the baseline under another project's key.
            string sessionKey = sessions.CurrentSessionKey;
            PaletteRoleColor[]? parsedPaletteRoleColors = ParsePaletteRoleColors(paletteRoleColors);
            IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
            var stills = new List<PreflightStillFrame>(sampleTimes.Count);
            string normalizedPrefix = NormalizeOutputPath(outputPrefix ?? CreateDefaultOutputBasename("preflight"));

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

            MotionVariationResponse? motion = null;
            if (!staticLayout)
            {
                motion = await motionVariationAnalyzer.AnalyzeAsync(
                    scene,
                    sampleTimes,
                    renderScale,
                    0.02,
                    48,
                    0.35,
                    0.90,
                    24,
                    cancellationToken).ConfigureAwait(false);
            }

            QualityReviewResponse quality = await qualityAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                sampleTimes.Count,
                renderScale,
                styleProfile,
                allowAllCaps,
                allowHardCuts,
                allowRectDominance,
                relaxAesthetics,
                allowStillness,
                allowDenseText,
                allowMultiObjectElements,
                allowMonochrome,
                allowMinimalDensity,
                plannedForegroundElementsPerShot,
                evaluateMotion: !staticLayout,
                videoType: videoType,
                beatTimesSeconds: beatTimesSeconds,
                paletteRoleColors: parsedPaletteRoleColors,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!staticLayout)
            {
                StoreQualityBaseline(
                    sessionKey,
                    sampleTimes,
                    CreateQualityAnalysisOptions(
                        videoType,
                        styleProfile,
                        renderScale,
                        allowAllCaps,
                        allowHardCuts,
                        allowRectDominance,
                        relaxAesthetics,
                        allowStillness,
                        allowDenseText,
                        allowMultiObjectElements,
                        allowMonochrome,
                        allowMinimalDensity,
                        plannedForegroundElementsPerShot,
                        beatTimesSeconds,
                        parsedPaletteRoleColors),
                    quality,
                    stills.Select(still => still.OutputPath).ToArray());
            }

            List<string> blockers = [];
            if (!staticLayout && motion is not null && !motion.PassesMinimumMotion)
            {
                blockers.Add($"Motion variation did not pass: {motion.Verdict}.");
            }

            if (!quality.PassesQualityGate)
            {
                blockers.Add("evaluate_edit_quality reported critical or major issues.");
            }

            if (!staticLayout && requireAnimatedProperties && quality.Metrics.MotionContinuity.AnimatedPropertyCount == 0)
            {
                blockers.Add("animatedPropertyCount is 0; add explicit transform, opacity, brush, effect, or typography animation before exporting motion graphics.");
            }

            if (stills.Any(item => item.Warnings.Count > 0))
            {
                blockers.Add("One or more representative still renders returned visibility warnings.");
            }

            bool ready = blockers.Count == 0;
            return new FinalPreflightResponse(
                staticLayout ? false : ready,
                blockers,
                stills,
                motion,
                quality,
                ready ? (staticLayout ? "render_storyboard" : "export_video") : "suggest_quality_fixes")
            {
                ReadyForStoryboard = staticLayout && ready
            };
        });
    }

    [McpServerTool(Name = "compare_revisions")]
    [Description("Compares the current scene against the cached quality baseline from the last successful rendered evaluate_edit_quality or final_preflight call. Re-renders the same sample times with the same quality options, reports numeric metric deltas, resolved/introduced issues, a cross-axis regression flag, and paired previous/current still paths. Pass returnImageContent:true to append paired PNG previews.")]
    public ValueTask<CallToolResult> CompareRevisions(
        [Description("When true, append paired previous/current image/png MCP content blocks for every sampled still pair. Default false preserves JSON-only output.")]
        bool returnImageContent = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMcpManyAsync<CompareRevisionsResponse>(async () =>
        {
            QualityReviewBaseline baseline = sessions.GetQualityReviewBaseline();
            Scene scene = RequireSceneSnapshot();
            // Capture the session key alongside the snapshot: a switch during the render must not file
            // the refreshed baseline under another project's key.
            string sessionKey = sessions.CurrentSessionKey;
            IReadOnlyList<string> currentStillPaths = await RenderBaselineStillsAsync(
                scene,
                baseline.SampleTimes,
                baseline.Options.RenderScale,
                "revision-current",
                cancellationToken).ConfigureAwait(false);
            QualityReviewResponse current = await qualityAnalyzer.AnalyzeAsync(
                scene,
                baseline.SampleTimes,
                baseline.SampleTimes.Count,
                baseline.Options.RenderScale,
                baseline.Options.StyleProfile,
                baseline.Options.AllowAllCaps,
                baseline.Options.AllowHardCuts,
                baseline.Options.AllowRectDominance,
                baseline.Options.RelaxAesthetics,
                baseline.Options.AllowStillness,
                baseline.Options.AllowDenseText,
                baseline.Options.AllowMultiObjectElements,
                baseline.Options.AllowMonochrome,
                baseline.Options.AllowMinimalDensity,
                baseline.Options.PlannedForegroundElementsPerShot,
                evaluateMotion: true,
                videoType: baseline.Options.VideoType,
                beatTimesSeconds: baseline.Options.BeatTimesSeconds,
                paletteRoleColors: baseline.Options.PaletteRoleColors,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RevisionStillPair[] pairs = baseline.SampleTimes
                .Select((time, index) => new RevisionStillPair(
                    index,
                    Math.Round(time.TotalSeconds, 4, MidpointRounding.AwayFromZero),
                    index < baseline.StillPaths.Count ? baseline.StillPaths[index] : string.Empty,
                    index < currentStillPaths.Count ? currentStillPaths[index] : string.Empty))
                .ToArray();
            CompareRevisionsResponse response = new(
                SchemaVersion.Current,
                BuildMetricDeltas(baseline.Review, current),
                FindResolvedIssues(baseline.Review, current),
                FindIntroducedIssues(baseline.Review, current),
                HasSeverityRegressionAcrossAxes(baseline.Review, current),
                pairs,
                baseline.Review,
                current);

            StoreQualityBaseline(sessionKey, baseline.SampleTimes, baseline.Options, current, currentStillPaths);

            IReadOnlyList<ImageContentBlock> images = returnImageContent
                ? CreateRevisionPreviewImages(pairs)
                : [];
            return (response, images);
        });
    }

    [McpServerTool(Name = "export_video")]
    [Description("Exports the current scene through a registered headless encoder to a workspace-relative output path. Bare filenames are written under agent-output/. Control size/quality with crf or bitrate. Pass background:true to run as a job and poll read_render_job(jobId).")]
    public ValueTask<ToolResult<ExportVideoResult>> ExportVideo(
        [Description("Workspace-relative or in-workspace absolute output path. Render outputs use outputPath/outputDirectory; project file tools use path. Bare filenames are written under agent-output/. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Frame-rate numerator.")]
        int frameRateNumerator = 30,
        [Description("Frame-rate denominator.")]
        int frameRateDenominator = 1,
        [Description("Audio sample rate.")]
        int sampleRate = 44100,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Constant Rate Factor for the H.264/x265 encoder (0-51, lower is higher quality/larger file; libx264 default is 23). Mutually exclusive with bitrate. Raise it (e.g. 28-30) to shrink hard-to-compress content such as full-frame grain.")]
        int? crf = null,
        [Description("Target average video bitrate in bits per second (e.g. 4000000). Mutually exclusive with crf; forces ABR by dropping the crf option.")]
        int? bitrate = null,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        [Description("When true, run the export as a background job and return {status:running, jobId} immediately; poll read_render_job(jobId) for completion. Do not issue apply_edit while a background export is running.")]
        bool background = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<ExportVideoResult>(async () =>
        {
            // Only preflight-reject when FFmpeg is the sole encoder for this container; a non-FFmpeg
            // encoder (e.g. AVFoundation for macOS .mp4/.mov) can export without the worker.
            if (videoExporter.RequiresFFmpegWorker(NormalizeOutputPath(outputPath))
                && !FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.CodecUnavailable,
                    "export_video requires the Beutl.FFmpegWorker next to this MCP host, but it was not found under the server directory.",
                    "exportVideo",
                    "The stdio MCP host (source-run or standalone install) does not place the FFmpeg worker next to the server. Use the in-app MCP endpoint (Tools > AI Agents) for video export, or run from the installed Beutl app directory where the worker is copied under FFmpegWorker/."));
            }

            if (frameRateNumerator <= 0 || frameRateDenominator <= 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "Frame-rate numerator and denominator must be positive."));
            }

            if (crf is int crfValue && (crfValue < 0 || crfValue > 51))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "crf must be between 0 and 51."));
            }

            if (bitrate is int bitrateValue && bitrateValue <= 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "bitrate must be positive."));
            }

            if (crf.HasValue && bitrate.HasValue)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "Provide either crf or bitrate, not both."));
            }

            Scene scene = RequireSceneSnapshot(forceClone: background);
            string resolvedPath = workspace.ResolveForWrite(NormalizeOutputPath(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);

            async Task<ExportVideoResponse> RunExportAsync(CancellationToken token)
            {
                // Re-verify at write time: background jobs are serialized, so a preceding job may
                // have created this file after the pre-flight overwrite check passed.
                destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
                ExportVideoResponse exported = await videoExporter.ExportAsync(
                    scene,
                    resolvedPath,
                    new Rational(frameRateNumerator, frameRateDenominator),
                    sampleRate,
                    renderScale,
                    token,
                    crf,
                    bitrate).ConfigureAwait(false);
                sessions.RecordCreativeDirection(CreateExportCreativeFingerprint(scene, resolvedPath));
                return exported;
            }

            if (background)
            {
                string jobId = renderJobs.Enqueue(
                    "export",
                    async token => JsonSerializer.SerializeToNode(
                        await RunExportAsync(token).ConfigureAwait(false),
                        s_jobResultOptions)!);
                return new ExportVideoResult("running", jobId, null);
            }

            ExportVideoResponse response = await RunExportAsync(cancellationToken).ConfigureAwait(false);
            return new ExportVideoResult("completed", null, response);
        });
    }

    [McpServerTool(Name = "read_render_job")]
    [Description("Reports the status of a background render/export job started with background:true on render_storyboard or export_video. Poll until state is 'completed' (result holds the render_storyboard/export_video payload), 'failed' (error explains why), or 'cancelled'.")]
    public ToolResult<RenderJobSnapshot> ReadRenderJob(
        [Description("Job id returned by a background render_storyboard/export_video call.")]
        string jobId)
    {
        return Execute(() => RequireRenderJob(jobId));
    }

    [McpServerTool(Name = "cancel_render_job")]
    [Description("Requests cancellation of a running background render/export job. Returns the job snapshot; a still-running job transitions to 'cancelled' once it observes the request.")]
    public ToolResult<RenderJobSnapshot> CancelRenderJob(
        [Description("Job id to cancel.")]
        string jobId)
    {
        return Execute(() =>
        {
            renderJobs.Cancel(jobId);
            return RequireRenderJob(jobId);
        });
    }

    private RenderJobSnapshot RequireRenderJob(string jobId)
    {
        return renderJobs.Get(jobId)
               ?? throw new ReconcileException(new ToolError(
                   ErrorCode.StaleHandle,
                   $"No render job with id '{jobId}' exists.",
                   jobId));
    }

    private static void ValidateVideoType(string? videoType)
    {
        if (!string.IsNullOrWhiteSpace(videoType))
        {
            VideoTypeCatalog.Resolve(videoType);
        }
    }

    private static async ValueTask<CallToolResult> ExecuteMcpAsync<T>(
        Func<ValueTask<(T Value, ImageContentBlock? Image)>> action)
    {
        try
        {
            (T value, ImageContentBlock? image) = await action().ConfigureAwait(false);
            return ToCallToolResult(ToolResult<T>.Success(value), image);
        }
        catch (Exception ex)
        {
            ToolError error = ToolErrorMapper.Map(ex);
            return ToCallToolResult(ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint));
        }
    }

    private static async ValueTask<CallToolResult> ExecuteMcpManyAsync<T>(
        Func<ValueTask<(T Value, IReadOnlyList<ImageContentBlock> Images)>> action)
    {
        try
        {
            (T value, IReadOnlyList<ImageContentBlock> images) = await action().ConfigureAwait(false);
            return ToCallToolResult(ToolResult<T>.Success(value), images);
        }
        catch (Exception ex)
        {
            ToolError error = ToolErrorMapper.Map(ex);
            return ToCallToolResult(ToolResult<T>.Failure(error.Code, error.Message, error.Target, error.Hint), []);
        }
    }

    private static CallToolResult ToCallToolResult<T>(ToolResult<T> result, ImageContentBlock? image = null)
        => ToCallToolResult(result, image is null ? [] : [image]);

    private static CallToolResult ToCallToolResult<T>(ToolResult<T> result, IReadOnlyList<ImageContentBlock> images)
    {
        var content = new List<ContentBlock>
        {
            new TextContentBlock
            {
                Text = JsonSerializer.Serialize(result, s_toolResultOptions)
            }
        };
        if (result.IsSuccess)
        {
            content.AddRange(images);
        }

        return new CallToolResult
        {
            Content = content,
            IsError = false
        };
    }

    private static IReadOnlyList<TimeSpan>? NormalizeQualitySampleTimesOrNull(double[]? timeSeconds)
    {
        if (timeSeconds is not { Length: > 0 })
        {
            return null;
        }

        return timeSeconds
            .Where(double.IsFinite)
            .Select(seconds => TimeSpan.FromSeconds(Math.Max(0, seconds)))
            .Distinct()
            .OrderBy(time => time)
            .ToArray();
    }

    private static IReadOnlyList<TimeSpan> ResolveQualitySampleTimes(Scene scene, double[]? timeSeconds, int sampleCount)
    {
        IReadOnlyList<TimeSpan>? explicitTimes = NormalizeQualitySampleTimesOrNull(timeSeconds);
        if (explicitTimes is { Count: >= 2 })
        {
            TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
            return explicitTimes
                .Select(time => time > duration ? duration : time)
                .Distinct()
                .OrderBy(time => time)
                .ToArray();
        }

        int count = Math.Clamp(sampleCount, 2, 8);
        double durationSeconds = scene.Duration > TimeSpan.Zero ? scene.Duration.TotalSeconds : 1;
        return Enumerable
            .Range(0, count)
            .Select(index => TimeSpan.FromSeconds(durationSeconds * (index + 0.5) / count))
            .ToArray();
    }

    private static PaletteRoleColor[]? ParsePaletteRoleColors(JsonElement? paletteRoleColors)
    {
        if (paletteRoleColors is null)
        {
            return null;
        }

        JsonElement element = paletteRoleColors.Value;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? json = element.GetString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return ParsePaletteRoleColorArray(document.RootElement);
            }
            catch (JsonException)
            {
                throw PaletteRoleColorsValidationError("paletteRoleColors must be a JSON array string containing { role, color } objects.");
            }
        }

        return ParsePaletteRoleColorArray(element);
    }

    private static PaletteRoleColor[] ParsePaletteRoleColorArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw PaletteRoleColorsValidationError("paletteRoleColors must be an array of { role, color } objects or a JSON string containing that array.");
        }

        var result = new List<PaletteRoleColor>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw PaletteRoleColorsValidationError($"paletteRoleColors[{index}] must be an object with role and color string properties.");
            }

            string? role = GetStringProperty(item, "role");
            string? color = GetStringProperty(item, "color");
            if (string.IsNullOrWhiteSpace(role))
            {
                throw PaletteRoleColorsValidationError($"paletteRoleColors[{index}].role must be a non-empty string.");
            }

            if (!IsRgbHexColor(color))
            {
                throw PaletteRoleColorsValidationError($"paletteRoleColors[{index}].color must be a #RRGGBB string.");
            }

            result.Add(new PaletteRoleColor(role, color!));
            index++;
        }

        return result.ToArray();
    }

    private static string? GetStringProperty(JsonElement item, string name)
    {
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsRgbHexColor(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        foreach (char c in value.AsSpan(1))
        {
            if (!IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexDigit(char value)
    {
        return value is >= '0' and <= '9'
               or >= 'a' and <= 'f'
               or >= 'A' and <= 'F';
    }

    private static ReconcileException PaletteRoleColorsValidationError(string message)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            message,
            "paletteRoleColors",
            "Pass paletteRoleColors as [{\"role\":\"bg-base\",\"color\":\"#102030\"}] or as a JSON string containing that array."));
    }

    private static QualityAnalysisOptions CreateQualityAnalysisOptions(
        string? videoType,
        string? styleProfile,
        float renderScale,
        bool allowAllCaps,
        bool allowHardCuts,
        bool allowRectDominance,
        bool relaxAesthetics,
        bool allowStillness,
        bool allowDenseText,
        bool allowMultiObjectElements,
        bool allowMonochrome,
        bool allowMinimalDensity,
        double plannedForegroundElementsPerShot,
        double[]? beatTimesSeconds,
        PaletteRoleColor[]? paletteRoleColors)
    {
        return new QualityAnalysisOptions(
            videoType,
            styleProfile,
            float.IsFinite(renderScale) && renderScale > 0 ? renderScale : 1,
            allowAllCaps,
            allowHardCuts,
            allowRectDominance,
            relaxAesthetics,
            allowStillness,
            allowDenseText,
            allowMultiObjectElements,
            allowMonochrome,
            allowMinimalDensity,
            plannedForegroundElementsPerShot,
            beatTimesSeconds?.Where(double.IsFinite).ToArray(),
            paletteRoleColors?.ToArray());
    }

    private async ValueTask<IReadOnlyList<string>> RenderBaselineStillsAsync(
        Scene scene,
        IReadOnlyList<TimeSpan> sampleTimes,
        float renderScale,
        string prefix,
        CancellationToken cancellationToken)
    {
        string batchId = Guid.NewGuid().ToString("N")[..8];
        var stillPaths = new List<string>(sampleTimes.Count);
        for (int i = 0; i < sampleTimes.Count; i++)
        {
            TimeSpan time = sampleTimes[i];
            string stillPath = Path.Combine(
                "agent-output",
                $"{prefix}-{batchId}-still-{i:D2}-{Math.Max(0, (long)Math.Round(time.TotalMilliseconds)):D8}ms.png");
            string resolvedPath = workspace.ResolveForWrite(stillPath);
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, false);
            RenderStillResponse still = await stillRenderer.RenderAsync(
                scene,
                time,
                resolvedPath,
                renderScale,
                cancellationToken).ConfigureAwait(false);
            stillPaths.Add(still.OutputPath);
        }

        return stillPaths;
    }

    private void StoreQualityBaseline(
        string sessionKey,
        IReadOnlyList<TimeSpan> sampleTimes,
        QualityAnalysisOptions options,
        QualityReviewResponse review,
        IReadOnlyList<string> stillPaths)
    {
        sessions.StoreQualityReviewBaseline(new QualityReviewBaseline(
            sessionKey,
            DateTimeOffset.UtcNow,
            sampleTimes.ToArray(),
            options with
            {
                BeatTimesSeconds = options.BeatTimesSeconds?.ToArray(),
                PaletteRoleColors = options.PaletteRoleColors?.ToArray()
            },
            review,
            stillPaths.ToArray()));
    }

    private static IReadOnlyList<ImageContentBlock> CreateRevisionPreviewImages(IReadOnlyList<RevisionStillPair> pairs)
    {
        var images = new List<ImageContentBlock>(pairs.Count * 2);
        foreach (RevisionStillPair pair in pairs)
        {
            if (File.Exists(pair.PreviousPath))
            {
                images.Add(ImageContentBlock.FromBytes(
                    ImagePreviewEncoder.EncodePngFile(pair.PreviousPath),
                    "image/png"));
            }

            if (File.Exists(pair.CurrentPath))
            {
                images.Add(ImageContentBlock.FromBytes(
                    ImagePreviewEncoder.EncodePngFile(pair.CurrentPath),
                    "image/png"));
            }
        }

        return images;
    }

    private static IReadOnlyList<QualityMetricDelta> BuildMetricDeltas(
        QualityReviewResponse previous,
        QualityReviewResponse current)
    {
        var deltas = new List<QualityMetricDelta>();
        AddDelta(deltas, "typography.textObjectCount", previous.Metrics.Typography.TextObjectCount, current.Metrics.Typography.TextObjectCount);
        AddDelta(deltas, "typography.lowContrastTextCount", previous.Metrics.Typography.LowContrastTextCount, current.Metrics.Typography.LowContrastTextCount);
        AddDelta(deltas, "palette.harmonyScore", previous.Metrics.Palette.HarmonyScore, current.Metrics.Palette.HarmonyScore);
        AddDelta(deltas, "palette.hueRelationshipScore", previous.Metrics.Palette.HueRelationshipScore, current.Metrics.Palette.HueRelationshipScore);
        AddDelta(deltas, "backgroundRichness.fullFrameBackgroundLayerCount", previous.Metrics.BackgroundRichness.FullFrameBackgroundLayerCount, current.Metrics.BackgroundRichness.FullFrameBackgroundLayerCount);
        AddDelta(deltas, "backgroundRichness.flatSingleLayerBackgroundCount", previous.Metrics.BackgroundRichness.FlatSingleLayerBackgroundCount, current.Metrics.BackgroundRichness.FlatSingleLayerBackgroundCount);
        AddDelta(deltas, "layerDensity.averageVisibleLayerCount", previous.Metrics.LayerDensity.AverageVisibleLayerCount, current.Metrics.LayerDensity.AverageVisibleLayerCount);
        AddDelta(deltas, "layerDensity.averageForegroundLayerCount", previous.Metrics.LayerDensity.AverageForegroundLayerCount, current.Metrics.LayerDensity.AverageForegroundLayerCount);
        AddDelta(deltas, "tempo.timelineEventsPerSecond", previous.Metrics.Tempo.TimelineEventsPerSecond, current.Metrics.Tempo.TimelineEventsPerSecond);
        AddDelta(deltas, "tempo.longForegroundGapCount", previous.Metrics.Tempo.LongForegroundGapCount, current.Metrics.Tempo.LongForegroundGapCount);
        AddDelta(deltas, "motionContinuity.minimumChangedPixelRatio", previous.Metrics.MotionContinuity.MinimumChangedPixelRatio, current.Metrics.MotionContinuity.MinimumChangedPixelRatio);
        AddDelta(deltas, "motionContinuity.averageChangedPixelRatio", previous.Metrics.MotionContinuity.AverageChangedPixelRatio, current.Metrics.MotionContinuity.AverageChangedPixelRatio);
        AddDelta(deltas, "motionContinuity.hardCutLikeBoundaryCount", previous.Metrics.MotionContinuity.HardCutLikeBoundaryCount, current.Metrics.MotionContinuity.HardCutLikeBoundaryCount);

        if (previous.Metrics.TransitionVocabulary is not null || current.Metrics.TransitionVocabulary is not null)
        {
            AddDelta(
                deltas,
                "transitionVocabulary.boundaryCount",
                previous.Metrics.TransitionVocabulary?.Boundaries.Count ?? 0,
                current.Metrics.TransitionVocabulary?.Boundaries.Count ?? 0);
            string[] types = (previous.Metrics.TransitionVocabulary?.Histogram.Keys ?? Enumerable.Empty<string>())
                .Concat(current.Metrics.TransitionVocabulary?.Histogram.Keys ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (string type in types)
            {
                AddDelta(
                    deltas,
                    $"transitionVocabulary.histogram.{type}",
                    previous.Metrics.TransitionVocabulary?.Histogram.GetValueOrDefault(type) ?? 0,
                    current.Metrics.TransitionVocabulary?.Histogram.GetValueOrDefault(type) ?? 0);
            }
        }

        if (previous.Metrics.PaletteBalance is not null || current.Metrics.PaletteBalance is not null)
        {
            AddDelta(
                deltas,
                "paletteBalance.neutralShare",
                previous.Metrics.PaletteBalance?.NeutralShare ?? 0,
                current.Metrics.PaletteBalance?.NeutralShare ?? 0);
            string[] roles = (previous.Metrics.PaletteBalance?.RoleShares.Select(item => item.Role) ?? Enumerable.Empty<string>())
                .Concat(current.Metrics.PaletteBalance?.RoleShares.Select(item => item.Role) ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (string role in roles)
            {
                AddDelta(
                    deltas,
                    $"paletteBalance.roleShare.{role}",
                    FindRoleShare(previous.Metrics.PaletteBalance, role),
                    FindRoleShare(current.Metrics.PaletteBalance, role));
            }
        }

        return deltas;
    }

    private static double FindRoleShare(PaletteBalanceMetrics? metrics, string role)
    {
        return metrics?.RoleShares
            .FirstOrDefault(item => string.Equals(item.Role, role, StringComparison.Ordinal))
            ?.Share ?? 0;
    }

    private static void AddDelta(List<QualityMetricDelta> deltas, string metric, double previous, double current)
    {
        deltas.Add(new QualityMetricDelta(
            metric,
            Math.Round(previous, 4, MidpointRounding.AwayFromZero),
            Math.Round(current, 4, MidpointRounding.AwayFromZero),
            Math.Round(current - previous, 4, MidpointRounding.AwayFromZero)));
    }

    private static IReadOnlyList<QualityIssue> FindResolvedIssues(
        QualityReviewResponse previous,
        QualityReviewResponse current)
    {
        HashSet<string> currentKeys = current.Issues.Select(CreateIssueIdentity).ToHashSet(StringComparer.Ordinal);
        return previous.Issues
            .Where(issue => !currentKeys.Contains(CreateIssueIdentity(issue)))
            .ToArray();
    }

    private static IReadOnlyList<QualityIssue> FindIntroducedIssues(
        QualityReviewResponse previous,
        QualityReviewResponse current)
    {
        HashSet<string> previousKeys = previous.Issues.Select(CreateIssueIdentity).ToHashSet(StringComparer.Ordinal);
        return current.Issues
            .Where(issue => !previousKeys.Contains(CreateIssueIdentity(issue)))
            .ToArray();
    }

    private static bool HasSeverityRegressionAcrossAxes(QualityReviewResponse previous, QualityReviewResponse current)
    {
        Dictionary<string, int> previousRanks = BuildCategorySeverityRanks(previous);
        Dictionary<string, int> currentRanks = BuildCategorySeverityRanks(current);
        string[] categories = previousRanks.Keys
            .Concat(currentRanks.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        bool worsened = categories.Any(category =>
            currentRanks.GetValueOrDefault(category, 3) <= previousRanks.GetValueOrDefault(category, 3) - 1);
        bool improved = categories.Any(category =>
            currentRanks.GetValueOrDefault(category, 3) >= previousRanks.GetValueOrDefault(category, 3) + 1);
        return worsened && improved;
    }

    private static Dictionary<string, int> BuildCategorySeverityRanks(QualityReviewResponse review)
    {
        return review.Issues
            .GroupBy(issue => issue.Category, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Min(issue => SeverityRank(issue.Severity)),
                StringComparer.Ordinal);
    }

    private static string CreateIssueIdentity(QualityIssue issue)
    {
        return string.Join(
            "\u001f",
            issue.Category,
            issue.Severity,
            issue.Message,
            issue.Evidence,
            string.Join(",", issue.ElementIds.Order(StringComparer.Ordinal)),
            string.Join(",", issue.ObjectIds.Order(StringComparer.Ordinal)));
    }

    private static CreativeDirectionFingerprint CreateExportCreativeFingerprint(Scene scene, string outputPath)
    {
        string concept = string.IsNullOrWhiteSpace(scene.Name)
            ? Path.GetFileNameWithoutExtension(outputPath)
            : scene.Name;
        string[] paletteRoles = scene.Children
            .Select(element => element.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(name => name.Split(['[', ']', ':', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(token => token.Contains("palette", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("color", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("accent", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("background", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        string[] motionVerbs = scene.Children
            .Select(element => element.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(name => name.Split([' ', '[', ']', ':', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(token => token.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("drift", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("pulse", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("reveal", StringComparison.OrdinalIgnoreCase)
                            || token.Contains("settle", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        string timingSignature = string.Join(
            " | ",
            scene.Children
                .OrderBy(element => element.Start)
                .ThenBy(element => element.ZIndex)
                .Take(10)
                .Select(element => $"{NormalizeFingerprintToken(element.Name)}@{element.Start.TotalSeconds:0.##}+{element.Length.TotalSeconds:0.##}"));
        if (string.IsNullOrWhiteSpace(timingSignature))
        {
            timingSignature = "empty-scene-export";
        }

        return new CreativeDirectionFingerprint(
            concept,
            paletteRoles,
            motionVerbs,
            timingSignature,
            DateTimeOffset.UtcNow);
    }

    private static string NormalizeFingerprintToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "element";
        }

        return string.Join(
            "-",
            value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(4));
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

    private string CreateDefaultOutputBasename(string stem)
        => $"{stem}-{ResolveOutputToken()}";

    private string ResolveOutputToken()
    {
        string? sessionId = sessions.CurrentSession?.SessionId;
        return SanitizeFileToken(string.IsNullOrWhiteSpace(sessionId)
            ? s_processOutputToken
            : sessionId);
    }

    private static string CreateProcessOutputToken()
        => $"process-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";

    private static string SanitizeFileToken(string token)
    {
        string trimmed = token.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(trimmed) ? s_processOutputToken : trimmed;
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

    private static void SaveStoryboardStill(Bitmap bitmap, string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!bitmap.Save(outputPath, EncodedImageFormat.Png))
        {
            throw new IOException($"Failed to write storyboard still image to '{outputPath}'.");
        }
    }

    private static CutEyeTrace[] BuildCutEyeTrace(IReadOnlyList<StoryboardEyeTraceFrame> frames)
    {
        if (frames.Count < 2)
        {
            return [];
        }

        var result = new CutEyeTrace[frames.Count - 1];
        for (int i = 1; i < frames.Count; i++)
        {
            StoryboardEyeTraceFrame left = frames[i - 1];
            StoryboardEyeTraceFrame right = frames[i];
            double dx = left.FocalPoint.X - right.FocalPoint.X;
            double dy = left.FocalPoint.Y - right.FocalPoint.Y;
            double displacement = Math.Sqrt((dx * dx) + (dy * dy)) / Math.Sqrt(2);
            double roundedDisplacement = Math.Round(displacement, 4, MidpointRounding.AwayFromZero);
            result[i - 1] = new CutEyeTrace(
                left.Name,
                right.Name,
                left.FocalPoint,
                right.FocalPoint,
                roundedDisplacement,
                roundedDisplacement > 0.33);
        }

        return result;
    }

    private static IReadOnlyList<ResolvedStoryboardShot> ResolveStoryboardShots(
        Scene scene,
        StoryboardShotInput[]? shots)
    {
        if (shots is { Length: > 0 })
        {
            ResolvedStoryboardShot[] explicitShots = shots
                .Where(shot => double.IsFinite(shot.TimeSeconds))
                .Select((shot, index) => new ResolvedStoryboardShot(
                    string.IsNullOrWhiteSpace(shot.Name) ? $"shot-{index + 1}" : shot.Name.Trim(),
                    TimeSpan.FromSeconds(Math.Max(0, shot.TimeSeconds))))
                .OrderBy(shot => shot.Time)
                .ToArray();
            if (explicitShots.Length > 0)
            {
                return explicitShots;
            }
        }

        ResolvedStoryboardShot[] derivedShots = scene.Children
            .OrderBy(element => element.Start)
            .ThenBy(element => element.ZIndex)
            .Select(element =>
            {
                TimeSpan midpoint = element.Length > TimeSpan.Zero
                    ? element.Start + TimeSpan.FromTicks(element.Length.Ticks / 2)
                    : element.Start;
                return new ResolvedStoryboardShot(
                    string.IsNullOrWhiteSpace(element.Name) ? element.Id.ToString() : element.Name,
                    midpoint);
            })
            .GroupBy(shot => shot.Time)
            .Select(group => group.First())
            .OrderBy(shot => shot.Time)
            .ToArray();
        if (derivedShots.Length > 0)
        {
            return derivedShots;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "render_storyboard requires explicit shots or at least one timeline Element."));
    }

    internal static IReadOnlyList<ResolvedStoryboardFrame> ResolveStoryboardFrames(
        Scene scene,
        StoryboardShotInput[]? shots,
        int subdivisionLevel)
        => ResolveStoryboardFrames(scene, shots, null, subdivisionLevel);

    internal static IReadOnlyList<ResolvedStoryboardFrame> ResolveStoryboardFrames(
        Scene scene,
        StoryboardShotInput[]? shots,
        double[]? timeSeconds,
        int subdivisionLevel)
    {
        IReadOnlyList<ResolvedStoryboardShot> anchors = ResolveExplicitStoryboardTimes(scene, timeSeconds)
                                                        ?? ResolveStoryboardShots(scene, shots);
        int normalizedSubdivisionLevel = NormalizeStoryboardSubdivisionLevel(subdivisionLevel);
        if (normalizedSubdivisionLevel == 0)
        {
            return anchors
                .Select(shot => new ResolvedStoryboardFrame(
                    shot.Name,
                    shot.Time,
                    StoryboardFrameKindShot,
                    0))
                .ToArray();
        }

        TimeSpan dedupeTolerance = GetStoryboardDedupeTolerance(scene);
        ResolvedStoryboardShot[] dedupedAnchors = timeSeconds is null
            ? DeduplicateStoryboardAnchors(anchors, dedupeTolerance)
            : anchors.ToArray();
        if (dedupedAnchors.Length == 0)
        {
            return [];
        }

        int denominator = 1 << normalizedSubdivisionLevel;
        var frames = new List<ResolvedStoryboardFrame>(dedupedAnchors.Length * denominator);
        for (int i = 0; i < dedupedAnchors.Length; i++)
        {
            ResolvedStoryboardShot left = dedupedAnchors[i];
            frames.Add(new ResolvedStoryboardFrame(
                left.Name,
                left.Time,
                StoryboardFrameKindShot,
                0));

            if (i == dedupedAnchors.Length - 1)
            {
                continue;
            }

            ResolvedStoryboardShot right = dedupedAnchors[i + 1];
            TimeSpan gap = right.Time - left.Time;
            if (gap <= dedupeTolerance)
            {
                continue;
            }

            for (int numerator = 1; numerator < denominator; numerator++)
            {
                TimeSpan time = Interpolate(left.Time, right.Time, numerator, denominator);
                if (IsDuplicateStoryboardTime(time, left.Time, dedupeTolerance)
                    || IsDuplicateStoryboardTime(time, right.Time, dedupeTolerance)
                    || (frames.Count > 0 && IsDuplicateStoryboardTime(time, frames[^1].Time, dedupeTolerance)))
                {
                    continue;
                }

                (int reducedNumerator, int reducedDenominator, int frameSubdivisionLevel) =
                    ReduceBinaryFraction(numerator, denominator);
                frames.Add(new ResolvedStoryboardFrame(
                    CreateInbetweenName(
                        left.Name,
                        right.Name,
                        frameSubdivisionLevel,
                        reducedNumerator,
                        reducedDenominator),
                    time,
                    StoryboardFrameKindInbetween,
                    frameSubdivisionLevel));
            }
        }

        return frames;
    }

    private static IReadOnlyList<ResolvedStoryboardShot>? ResolveExplicitStoryboardTimes(
        Scene scene,
        double[]? timeSeconds)
    {
        if (timeSeconds is null)
        {
            return null;
        }

        if (timeSeconds.Length == 0)
        {
            throw CreateStoryboardTimeValidationError("render_storyboard timeSeconds must contain at least one scene time.");
        }

        TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
        var seen = new HashSet<TimeSpan>();
        var result = new List<ResolvedStoryboardShot>(timeSeconds.Length);
        for (int i = 0; i < timeSeconds.Length; i++)
        {
            double seconds = timeSeconds[i];
            if (!double.IsFinite(seconds))
            {
                throw CreateStoryboardTimeValidationError($"render_storyboard timeSeconds[{i}] must be finite.");
            }

            if (seconds < 0 || seconds > duration.TotalSeconds)
            {
                throw CreateStoryboardTimeValidationError(
                    $"render_storyboard timeSeconds[{i}]={seconds.ToString(CultureInfo.InvariantCulture)} is outside the scene range 0..{duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
            }

            TimeSpan time = TimeSpan.FromSeconds(seconds);
            if (!seen.Add(time))
            {
                throw CreateStoryboardTimeValidationError(
                    $"render_storyboard timeSeconds contains duplicate time {seconds.ToString(CultureInfo.InvariantCulture)}.");
            }

            result.Add(new ResolvedStoryboardShot(CreateExplicitStoryboardTimeName(seconds), time));
        }

        return result
            .OrderBy(shot => shot.Time)
            .ToArray();
    }

    private static string CreateExplicitStoryboardTimeName(double seconds)
        => $"t:{seconds.ToString("0.####", CultureInfo.InvariantCulture)}";

    private static ReconcileException CreateStoryboardTimeValidationError(string message)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            message,
            "timeSeconds",
            "Pass finite scene times in seconds within the scene duration, or omit timeSeconds to use explicit shots or auto Element midpoint detection."));
    }

    internal static int NormalizeStoryboardSubdivisionLevel(int subdivisionLevel)
    {
        return Math.Clamp(subdivisionLevel, 0, MaxStoryboardSubdivisionLevel);
    }

    private static void ValidateStoryboardFrameCount(int frameCount, int subdivisionLevel, string target = "subdivisionLevel")
    {
        if (frameCount <= MaxStoryboardFrameCount)
        {
            return;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"render_storyboard would render {frameCount} frames, above the limit of {MaxStoryboardFrameCount}. Lower subdivisionLevel or narrow the shots before rendering.",
            target,
            target == "timeSeconds"
                ? "Lower subdivisionLevel, pass fewer timeSeconds, or split the storyboard review into narrower time ranges."
                : subdivisionLevel > 0
                    ? "Lower subdivisionLevel, pass fewer shots, or split the storyboard review into narrower shot ranges."
                    : "Pass fewer shots or split the storyboard review into narrower shot ranges."));
    }

    private static ResolvedStoryboardShot[] DeduplicateStoryboardAnchors(
        IReadOnlyList<ResolvedStoryboardShot> anchors,
        TimeSpan tolerance)
    {
        var result = new List<ResolvedStoryboardShot>(anchors.Count);
        foreach (ResolvedStoryboardShot anchor in anchors)
        {
            if (result.Count == 0
                || !IsDuplicateStoryboardTime(anchor.Time, result[^1].Time, tolerance))
            {
                result.Add(anchor);
            }
        }

        return [.. result];
    }

    private static TimeSpan GetStoryboardDedupeTolerance(Scene scene)
    {
        int frameRate = GetSceneFrameRate(scene);
        return TimeSpan.FromSeconds(0.5d / frameRate);
    }

    private static int GetSceneFrameRate(Scene scene)
    {
        Project? project = scene.FindHierarchicalParent<Project>();
        if (project?.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value) == true
            && int.TryParse(value, out int rate)
            && rate > 0)
        {
            return rate;
        }

        return DefaultSceneFrameRate;
    }

    private static bool IsDuplicateStoryboardTime(TimeSpan left, TimeSpan right, TimeSpan tolerance)
    {
        return (left - right).Duration() <= tolerance;
    }

    private static TimeSpan Interpolate(TimeSpan left, TimeSpan right, int numerator, int denominator)
    {
        long ticks = left.Ticks + (long)Math.Round((right.Ticks - left.Ticks) * (numerator / (double)denominator));
        return TimeSpan.FromTicks(Math.Max(0, ticks));
    }

    private static (int Numerator, int Denominator, int SubdivisionLevel) ReduceBinaryFraction(
        int numerator,
        int denominator)
    {
        while (numerator % 2 == 0 && denominator % 2 == 0)
        {
            numerator /= 2;
            denominator /= 2;
        }

        int level = 0;
        for (int value = denominator; value > 1; value >>= 1)
        {
            level++;
        }

        return (numerator, denominator, level);
    }

    private static string CreateInbetweenName(
        string leftName,
        string rightName,
        int subdivisionLevel,
        int numerator,
        int denominator)
    {
        return $"between:{leftName}~{rightName}@L{subdivisionLevel}:{numerator}/{denominator}";
    }

    private static string NormalizeStoryboardDirectory(string outputDirectory)
    {
        return string.IsNullOrWhiteSpace(outputDirectory)
            ? "agent-output"
            : outputDirectory;
    }

    private static string NormalizeStoryboardBasename(string basename)
    {
        string normalized = string.IsNullOrWhiteSpace(basename)
            ? "storyboard"
            : Path.GetFileNameWithoutExtension(basename.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "storyboard";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '-');
        }

        return normalized;
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
            "backgroundRichness" => "Replace the flat single-layer background with a 3+ stop gradient or shader base, then add a midground texture/depth layer and subtle drift/parallax.",
            "shapeIntent" => "Rename/tag each large or animated foreground shape with a clear role and purpose, or remove it if it does not serve the shot.",
            "motionIntent" => "Rename/tag animated foreground shapes with a concrete motion job such as beat slide, scan sweep, pulse reveal, wipe transition, or impact burst.",
            "elementStructure" => "Split ordinary content into one Element per EngineObject; keep multi-object Elements only for IFlowOperator chains such as DrawableGroup, DrawableDecorator, SoundGroup, or Scene3D.",
            "layerDensity" => "Use metrics.layerDensity to find the sparse time bands, then add missing background/midground/foreground layers or revise the quantitativePlanSheet when the brief intentionally calls for minimal density.",
            "tempoRhythm" => "Convert the BPM target into a beat grid, add visible foreground boundaries every 1-2 beats, close long foreground event gaps, and keep normal foreground holds near 2-4 beats.",
            "textBackgroundFit" => "Use an explicit [role:text-backing] shape only for real text plates, match its Start/Length to the text Element, then verify with measure_object_bounds.",
            "visualHierarchy" => "Keep one hero-scale text object per beat and reduce supporting labels below hero size and contrast.",
            "paletteHarmony" => "Re-derive the palette from one base hue with derive_palette; use a recognized harmony scheme, one saturated accent, and clear text/background luma separation.",
            "paletteBalance" => "Pass derive_palette role colors as paletteRoleColors, then reduce large accent-colored areas and restore the bg-base/background dominant role to carry most of the frame.",
            "effectIntent" => "Remove repeated decorative effect stacks or rename and keep only chains with one job: texture, hierarchy, transition energy, grade, or legibility.",
            "motionContinuity" => "Add bridged opacity/transform/spacing/brush/effect keyframes across cut boundaries and keep animatedPropertyCount above zero for motion graphics.",
            "cutRhythm" => "Bridge adjacent Elements with short overlaps, opacity fades, or transform continuation instead of pure hard boundaries.",
            "transitionVocabulary" => "Choose one continuity-editing transition vocabulary for the sequence, then revise outlier boundaries so dissolves, sweeps, dips, or hard cuts do not alternate without a clear reason.",
            "timelineCoverage" => "Close visible timeline gaps by extending adjacent clips/photos or adding a deliberate transition/black-gap Element.",
            _ => fallback
        };
    }

    internal Scene RequireSceneSnapshot(bool forceClone = false)
    {
        IEditingSession session = sessions.RequireSession();
        return CreateSceneSnapshot(session, forceClone);
    }

    internal static Scene CreateSceneSnapshot(IEditingSession session, bool forceClone = false)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.ReadOnSession(() =>
        {
            if (session.Root is not Scene scene)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The current editing session is not attached to a scene."));
            }

            // A background job outlives the request and a later apply_edit can mutate the live scene
            // while the renderer enumerates it, so background renders must clone file sessions too.
            if (!forceClone && session.Source != EditingSessionSource.LiveEditor)
            {
                return scene;
            }

            JsonObject snapshot = session.Documents.Read(scene);
            snapshot.Remove(SchemaVersion.PropertyName);
            if (scene.Uri is { } sceneUri)
            {
                snapshot["Uri"] = sceneUri.ToString();
            }

            var clone = (Scene)CoreSerializer.DeserializeFromJsonObject(
                snapshot,
                typeof(Scene),
                new CoreSerializerOptions
                {
                    BaseUri = scene.Uri,
                    Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
                });
            clone.Uri ??= scene.Uri;
            return clone;
        });
    }

    internal sealed record ResolvedStoryboardFrame(
        string Name,
        TimeSpan Time,
        string Kind,
        int SubdivisionLevel);

    internal sealed record ResolvedStoryboardShot(
        string Name,
        TimeSpan Time);

    private sealed record StoryboardEyeTraceFrame(
        string Name,
        NormalizedFocalPoint FocalPoint);
}

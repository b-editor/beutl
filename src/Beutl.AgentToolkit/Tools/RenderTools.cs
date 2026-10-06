using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record AnalyzeAudioRhythmResponse(
    string SchemaVersion,
    int SampleRate,
    AudioRhythmWindow AnalyzedWindow,
    double EstimatedBpm,
    double Confidence,
    IReadOnlyList<double> BeatTimesSeconds,
    IReadOnlyList<double> StrongOnsetTimesSeconds);

[McpServerToolType]
public sealed partial class RenderTools(
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard,
    StillRenderer stillRenderer,
    StoryboardRenderer storyboardRenderer,
    FrameDifferenceAnalyzer frameDifferenceAnalyzer,
    AudioRhythmAnalyzer audioRhythmAnalyzer,
    VideoExporter videoExporter,
    RenderJobManager renderJobs,
    IOutputOperationLeaseProvider outputOperations) : ToolBase
{
    private static readonly RenderJobProgressReporter NullProgress = RenderJobProgressReporter.Ignored;

    private static readonly JsonSerializerOptions s_jobResultOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);
    private static readonly string s_processOutputToken = CreateProcessOutputToken();
    private const int DefaultSceneFrameRate = 30;
    private const int MaxStoryboardFrameCount = 48;
    private const int MaxStoryboardSubdivisionLevel = 3;
    private const string StoryboardFrameKindShot = "shot";
    private const string StoryboardFrameKindInbetween = "inbetween";
    private const string RenderScaleDescription =
        "Supersampling render scale. Non-finite values and values <= 0 use 1. The ceil(frame size * renderScale) output extent must not exceed 16384 pixels on either axis, and less than that on a graphics device that attaches less; the rejection names the limit that applied.";
    private const string SampleCountDescription =
        "Number of evenly spaced samples when timeSeconds is omitted. Clamped to 2..8.";
    private const string ConfirmOverwriteDescription =
        "Required when outputPath already exists.";

    [McpServerTool(Name = "render_still")]
    [Description("Renders a still PNG from the current scene to a workspace-relative output path and returns image dimensions, active elements, and pixel measurements. Bare filenames are resolved directly within the workspace. By default the tool returns the same JSON text payload as before; pass returnImageContent:true to append a downscaled image/png content block for multimodal review.")]
    public ValueTask<CallToolResult> RenderStill(
        [Description("Workspace-relative or in-workspace absolute output path. Bare filenames are resolved directly within the workspace. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Scene time in seconds. Use this exact parameter name; time is not a render_still parameter.")]
        double timeSeconds = 0,
        [Description(RenderScaleDescription)]
        float renderScale = 1,
        [Description(ConfirmOverwriteDescription)]
        bool confirmOverwrite = false,
        [Description("When true, append a downscaled image/png MCP content block with a long edge of about 768 px. Default false preserves the path-only JSON response.")]
        bool returnImageContent = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMcpAsync<RenderStillResponse>(async () =>
        {
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            Scene scene = RequireSceneSnapshot();
            renderScale = ValidateRenderScale(scene, renderScale, "render_still");
            string resolvedPath = workspace.ResolveForWrite(outputPath);
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
        string outputDirectory = ".",
        [Description("Basename used for generated still PNGs and the contact sheet. Omit for a collision-free default containing the active session id; explicit values preserve exact filenames.")]
        string? basename = null,
        [Description(RenderScaleDescription)]
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
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            if (background && returnImageContent)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "returnImageContent cannot be combined with background:true; run render_storyboard synchronously when the contact-sheet image block is needed."));
            }

            // Capture the session once so a concurrent swap cannot pair an old-session snapshot with a
            // new-session frame rate. The rate comes from the live tree because the snapshot is a
            // Project-detached clone whose own frame-rate lookup would always miss.
            IEditingSession session = sessions.RequireSession();
            Scene scene = CreateSceneSnapshot(session);
            renderScale = ValidateRenderScale(scene, renderScale, "render_storyboard");
            int frameRate = ReadSessionFrameRate(session);
            int normalizedSubdivisionLevel = NormalizeStoryboardSubdivisionLevel(subdivisionLevel);
            IReadOnlyList<ResolvedStoryboardFrame> resolvedShots = ResolveStoryboardFrames(
                scene,
                shots,
                timeSeconds,
                normalizedSubdivisionLevel,
                frameRate);
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

            async Task<RenderStoryboardResponse> RunStoryboardAsync(RenderJobProgressReporter progress, CancellationToken token)
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
                progress.Report(0, plannedShots.Count, "rendering shots");
                foreach ((ResolvedStoryboardFrame shot, string resolvedPath) in plannedShots)
                {
                    progress.Report(renderedShots.Count, plannedShots.Count, "rendering shots");
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

                progress.Report(plannedShots.Count, plannedShots.Count, "contact sheet");
                storyboardRenderer.RenderContactSheet(contactSheetFrames, resolvedContactSheetPath);
                CutEyeTrace[] cutEyeTrace = BuildCutEyeTrace(eyeTraceFrames);
                return new RenderStoryboardResponse(resolvedContactSheetPath, renderedShots, cutEyeTrace);
            }

            if (background)
            {
                string jobId = outputOperation.Transfer(lease => renderJobs.Enqueue(
                    "storyboard",
                    async (progress, token) => JsonSerializer.SerializeToNode(
                        await RunStoryboardAsync(progress, token).ConfigureAwait(false),
                        s_jobResultOptions)!,
                    lease));
                return (new RenderStoryboardResult("running", jobId, null), (ImageContentBlock?)null);
            }

            RenderStoryboardResponse response = await RunStoryboardAsync(NullProgress, cancellationToken).ConfigureAwait(false);
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

    [McpServerTool(Name = "measure_frame_differences")]
    [Description("Renders selected scene times and returns pixel differences, foreground bounds, and coverage measurements. Returns no pass/fail, quality score, or completion verdict.")]
    public ValueTask<ToolResult<FrameDifferenceResponse>> MeasureFrameDifferences(
        [Description("Optional scene times in seconds. At least two distinct samples are required; omit to sample across the scene.")]
        double[]? timeSeconds = null,
        [Description(SampleCountDescription)]
        int sampleCount = 5,
        [Description(RenderScaleDescription)]
        float renderScale = 1,
        [Description("Absolute per-pixel channel delta used to count changed pixels. This is a measurement parameter, not a quality threshold.")]
        int pixelDeltaThreshold = 48,
        [Description("Threshold on the brightest color channel used to count foreground pixels.")]
        int foregroundLumaThreshold = 24,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            Scene scene = RequireSceneSnapshot();
            renderScale = ValidateRenderScale(scene, renderScale, "measure_frame_differences");
            IReadOnlyList<TimeSpan> sampleTimes = ResolveFrameSampleTimes(scene, timeSeconds, sampleCount);
            return await frameDifferenceAnalyzer.AnalyzeAsync(
                scene, sampleTimes, renderScale, pixelDeltaThreshold, foregroundLumaThreshold,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "analyze_audio_rhythm")]
    [Description("Decodes an audio/music-bed file through Beutl's audio source path and returns measured BPM, beat times, and strong onset times as measurement data. Reads are unrestricted; nonexistent paths return media_not_found.")]
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
                analysis.StrongOnsetTimesSeconds);
        });
    }

    [McpServerTool(Name = "export_video")]
    [Description("Exports the current scene through a registered headless encoder to a workspace-relative output path. Bare filenames are resolved directly within the workspace. Control size/quality with crf or bitrate. If AVFoundation is selected, a requested crf is ignored and reported in the successful result warnings. Pass background:true to run as a job and poll read_render_job(jobId).")]
    public ValueTask<ToolResult<ExportVideoResult>> ExportVideo(
        [Description("Workspace-relative or in-workspace absolute output path. Render outputs use outputPath/outputDirectory; project file tools use path. Bare filenames are resolved directly within the workspace. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Frame-rate numerator.")]
        int frameRateNumerator = 30,
        [Description("Frame-rate denominator.")]
        int frameRateDenominator = 1,
        [Description("Audio sample rate.")]
        int sampleRate = 44100,
        [Description(RenderScaleDescription)]
        float renderScale = 1,
        [Description("Constant Rate Factor for the FFmpeg H.264/x265 encoder (0-51, lower is higher quality/larger file; libx264 default is 23). Mutually exclusive with bitrate. AVFoundation has no CRF control; when it is selected, the successful result contains a warning that crf was ignored. Raise it (e.g. 28-30) to shrink hard-to-compress content such as full-frame grain.")]
        int? crf = null,
        [Description("Target average video bitrate in bits per second (e.g. 4000000). Mutually exclusive with crf; forces ABR by dropping the crf option. AVFoundation treats this as a target; the successful result includes a warning when the estimated video bitrate is below 50% of the request. The estimate subtracts configured audio bitrate when known and otherwise identifies its container-wide audio/muxing approximation.")]
        int? bitrate = null,
        [Description(ConfirmOverwriteDescription)]
        bool confirmOverwrite = false,
        [Description("When true, run the export as a background job and return {status:running, jobId} immediately; poll read_render_job(jobId) for completion. Do not issue apply_edit while a background export is running.")]
        bool background = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<ExportVideoResult>(async () =>
        {
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            Scene scene = RequireSceneSnapshot();
            renderScale = ValidateRenderScale(scene, renderScale, "export_video");

            // Only preflight-reject when FFmpeg is the sole encoder for this container; a non-FFmpeg
            // encoder (e.g. AVFoundation for macOS .mp4/.mov) can export without the worker.
            if (videoExporter.RequiresFFmpegWorker(outputPath)
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

            string resolvedPath = workspace.ResolveForWrite(outputPath);
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);

            async Task<ExportVideoResponse> RunExportAsync(RenderJobProgressReporter progress, CancellationToken token)
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
                    bitrate,
                    (encoded, total) => progress.Report((int)Math.Min(encoded, int.MaxValue), (int)Math.Min(total, int.MaxValue), "encoding")).ConfigureAwait(false);
                return exported;
            }

            if (background)
            {
                string jobId = outputOperation.Transfer(lease => renderJobs.Enqueue(
                    "export",
                    async (progress, token) => JsonSerializer.SerializeToNode(
                        await RunExportAsync(progress, token).ConfigureAwait(false),
                        s_jobResultOptions)!,
                    lease));
                return new ExportVideoResult("running", jobId, null);
            }

            ExportVideoResponse response = await RunExportAsync(NullProgress, cancellationToken).ConfigureAwait(false);
            return new ExportVideoResult("completed", null, response);
        });
    }

    [McpServerTool(Name = "read_render_job")]
    [Description("Reports the status of a background render/export job started with background:true on render_storyboard or export_video. Poll until state is 'completed' (result holds the render_storyboard/export_video payload), 'failed' (error explains why), or 'cancelled'. While it runs, progress carries {completed, total, ratio, stage} — shots rendered for a storyboard, frames encoded for an export — so a slow job can be told apart from a hung one without watching the output file grow. stage is 'queued' while another job holds the single render slot.")]
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

    private static IReadOnlyList<TimeSpan>? NormalizeFrameSampleTimesOrNull(double[]? timeSeconds)
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

    internal static IReadOnlyList<TimeSpan> ResolveFrameSampleTimes(Scene scene, double[]? timeSeconds, int sampleCount)
    {
        IReadOnlyList<TimeSpan>? explicitTimes = NormalizeFrameSampleTimesOrNull(timeSeconds);
        if (explicitTimes is not null && explicitTimes.Count < 2)
        {
            // A caller-supplied list that collapses to fewer than two distinct finite times must be
            // rejected, not silently replaced with auto-generated samples the caller never asked for.
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "timeSeconds must contain at least two distinct finite sample times.",
                "timeSeconds",
                "Provide two or more distinct sample times, or omit timeSeconds to use auto-generated samples."));
        }

        if (explicitTimes is { Count: >= 2 })
        {
            TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
            // TimeRange.Contains excludes the end, so clamp to the last renderable tick rather than the
            // exclusive Duration, which would sample a blank frame with no active elements.
            TimeSpan lastRenderable = duration - TimeSpan.FromTicks(1);
            TimeSpan[] clamped = explicitTimes
                .Select(time => time > lastRenderable ? lastRenderable : time)
                .Distinct()
                .OrderBy(time => time)
                .ToArray();

            // Multiple out-of-range times can all clamp to the last tick and collapse to one sample,
            // which FrameDifferenceAnalyzer would reject with an unmapped ArgumentException; surface a
            // typed validation error instead of leaking it.
            if (clamped.Length < 2)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "timeSeconds must contain at least two distinct times within the scene duration.",
                    "timeSeconds",
                    $"Provide two or more sample times inside [0, {duration.TotalSeconds:0.###}] seconds."));
            }

            return clamped;
        }

        int count = Math.Clamp(sampleCount, 2, 8);
        double durationSeconds = scene.Duration > TimeSpan.Zero ? scene.Duration.TotalSeconds : 1;
        return Enumerable
            .Range(0, count)
            .Select(index => TimeSpan.FromSeconds(durationSeconds * (index + 0.5) / count))
            .ToArray();
    }

    private OwnedOutputOperation BeginOutputOperation()
    {
        IDisposable lease = outputOperations.TryBeginOutputOperation()
                            ?? throw new OutputOperationBusyException();
        return new OwnedOutputOperation(lease);
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

    private sealed class OwnedOutputOperation(IDisposable lease) : IDisposable
    {
        private IDisposable? _lease = lease;

        public T Transfer<T>(Func<IDisposable, T> transfer)
        {
            ArgumentNullException.ThrowIfNull(transfer);
            IDisposable current = _lease
                                  ?? throw new InvalidOperationException("The output operation lease was already transferred.");
            T result = transfer(current);
            _lease = null;
            return result;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
        }
    }

    internal static float ValidateRenderScale(Scene scene, float renderScale, string toolName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        PixelSize frameSize = scene.FrameSize;

        // Validate against the allocator reached by StillRenderer or VideoExporter, not the engine ceiling.
        int maxDimension = BufferDimensionBudget.Resolve(BufferBudgetScope.Prediction).MaxDimension;
        double requestedWidth = GetRootDeviceExtent(frameSize.Width, normalizedScale);
        double requestedHeight = GetRootDeviceExtent(frameSize.Height, normalizedScale);
        if (requestedWidth <= maxDimension && requestedHeight <= maxDimension)
        {
            return normalizedScale;
        }

        double maximumScaleLimit = Math.Min(
            frameSize.Width > 0
                ? maxDimension / (double)frameSize.Width
                : double.PositiveInfinity,
            frameSize.Height > 0
                ? maxDimension / (double)frameSize.Height
                : double.PositiveInfinity);
        float maximumScale = (float)maximumScaleLimit;
        while (!RootOutputExtentFits(frameSize, maximumScale, maxDimension))
        {
            maximumScale = MathF.BitDecrement(maximumScale);
        }

        float nextScale = MathF.BitIncrement(maximumScale);
        while (float.IsFinite(nextScale) && RootOutputExtentFits(frameSize, nextScale, maxDimension))
        {
            maximumScale = nextScale;
            nextScale = MathF.BitIncrement(maximumScale);
        }

        string requestedExtent = $"{FormatDeviceExtent(requestedWidth)}x{FormatDeviceExtent(requestedHeight)}";
        string maximumScaleText = maximumScale.ToString("G9", CultureInfo.InvariantCulture);
        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"{toolName} renderScale {normalizedScale.ToString("G9", CultureInfo.InvariantCulture)} requests an output extent of {requestedExtent} pixels. "
            + $"Each output axis is limited to {maxDimension} pixels; "
            + $"the maximum usable renderScale for frame {frameSize.Width}x{frameSize.Height} is {maximumScaleText}.",
            "renderScale",
            $"Use renderScale <= {maximumScaleText} for this frame size."));
    }

    private static bool RootOutputExtentFits(PixelSize frameSize, float renderScale, int maxDimension)
    {
        return GetRootDeviceExtent(frameSize.Width, renderScale) <= maxDimension
               && GetRootDeviceExtent(frameSize.Height, renderScale) <= maxDimension;
    }

    private static float GetRootDeviceExtent(int logicalExtent, float renderScale)
    {
        return MathF.Ceiling(logicalExtent * renderScale);
    }

    private static string FormatDeviceExtent(double extent)
    {
        return extent >= long.MinValue && extent <= long.MaxValue
            ? ((long)extent).ToString(CultureInfo.InvariantCulture)
            : extent.ToString("G17", CultureInfo.InvariantCulture);
    }
}

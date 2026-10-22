using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Beutl;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;
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
    DestructiveGuard destructiveGuard,
    StillRenderer stillRenderer,
    StoryboardRenderer storyboardRenderer,
    FrameDifferenceAnalyzer frameDifferenceAnalyzer,
    AudioRhythmAnalyzer audioRhythmAnalyzer,
    VideoExporter videoExporter,
    RenderJobManager renderJobs,
    IOutputOperationLeaseProvider outputOperations) : ToolBase
{
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
    [Description("Renders a still PNG from the current scene to an absolute output path and returns image dimensions, active elements, and pixel measurements. Pass returnImageContent:true to append a downscaled image/png content block for multimodal review.")]
    public ValueTask<CallToolResult> RenderStill(
        [Description("Absolute output path. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Scene time in seconds. Use this exact parameter name; time is not a render_still parameter.")]
        double timeSeconds = 0,
        [Description(RenderScaleDescription)]
        float renderScale = 1,
        [Description(ConfirmOverwriteDescription)]
        bool confirmOverwrite = false,
        [Description("When true, append a downscaled image/png MCP content block with a long edge of about 768 px.")]
        bool returnImageContent = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMcpAsync<RenderStillResponse>(async () =>
        {
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            Scene scene = RequireSceneSnapshot();
            renderScale = ValidateRenderScale(scene, renderScale, "render_still");
            string resolvedPath = ToolPaths.ResolveForWrite(outputPath, nameof(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            RenderStillResponse response = await stillRenderer.RenderAsync(
                scene,
                MediaTime.FromSeconds(Math.Max(0, timeSeconds)),
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
    [Description("Renders a storyboard contact sheet from explicit sample times, explicit shots, or one auto-derived midpoint per timeline Element. Writes individual still PNGs and the contact sheet into outputDirectory. Pass timeSeconds for continuous single-shot pieces where Element-boundary shot detection would collapse the arc. Pass subdivisionLevel:1..3 to insert binary in-between frames between adjacent anchors for transition review. For scenes with many Elements this can exceed the MCP client request timeout; pass background:true to run it as a job and poll read_render_job(jobId) instead. Pass returnImageContent:true on a synchronous call to append one downscaled image/png contact-sheet content block.")]
    public ValueTask<CallToolResult> RenderStoryboard(
        [Description("Absolute output directory. Existing files require confirmOverwrite.")]
        string outputDirectory,
        [Description("Optional explicit storyboard shots. When omitted, one midpoint is derived per timeline Element.")]
        StoryboardShotInput[]? shots = null,
        [Description("Optional explicit scene times in seconds. When supplied, this overrides shots and auto shot detection entirely; each value becomes an anchor frame named t:<seconds>. Values must be finite, within the scene duration, non-empty, and stay within the 48-frame cap after subdivision.")]
        double[]? timeSeconds = null,
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
        [Description("Binary subdivision depth for in-between frames between adjacent shots. 0 renders one frame per shot; 1 adds midpoints, 2 adds quarter points, 3 adds eighth points. Values are clamped to 0..3.")]
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

            IEditingSession session = sessions.RequireSession();
            Scene scene = CreateSceneSnapshot(session);
            renderScale = ValidateRenderScale(scene, renderScale, "render_storyboard");
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
            string normalizedDirectory = ToolPaths.RequireAbsolute(outputDirectory, nameof(outputDirectory));
            string safeBasename = NormalizeStoryboardBasename(basename ?? $"storyboard-{session.SessionId}");
            StoryboardRenderPlan plan = PlanStoryboardOutputs(
                scene,
                renderScale,
                resolvedShots,
                normalizedDirectory,
                safeBasename,
                confirmOverwrite);

            if (background)
            {
                string jobId = outputOperation.Transfer(lease => renderJobs.Enqueue(
                    "storyboard",
                    async (progress, token) => JsonSerializer.SerializeToNode(
                        await RenderStoryboardFramesAsync(plan, progress, token).ConfigureAwait(false),
                        JsonSerializerOptions.Web)!,
                    lease));
                return (new RenderStoryboardResult("running", jobId, null), (ImageContentBlock?)null);
            }

            RenderStoryboardResponse response = await RenderStoryboardFramesAsync(plan, RenderJobProgressReporter.Ignored, cancellationToken).ConfigureAwait(false);
            ImageContentBlock? image = returnImageContent
                ? ImageContentBlock.FromBytes(
                    storyboardRenderer.RenderContactSheetPng(response.Shots, ImagePreviewEncoder.DefaultMaxLongEdge),
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
        [Description("Sum of a pixel's absolute red, green and blue deltas in sRGB (0-765) above which it counts as changed. This is a measurement parameter, not a quality threshold.")]
        int pixelDeltaThreshold = 48,
        [Description("Threshold on the brightest sRGB color channel (0-255) used to count foreground pixels.")]
        int foregroundLumaThreshold = 24,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            using OwnedOutputOperation outputOperation = BeginOutputOperation();
            Scene scene = RequireSceneSnapshot();
            renderScale = ValidateRenderScale(scene, renderScale, "measure_frame_differences");
            IReadOnlyList<MediaTime> sampleTimes = ResolveFrameSampleTimes(scene, timeSeconds, sampleCount);
            return await frameDifferenceAnalyzer.AnalyzeAsync(
                scene, sampleTimes, renderScale, pixelDeltaThreshold, foregroundLumaThreshold,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "analyze_audio_rhythm")]
    [Description("Decodes an audio/music-bed file through Beutl's audio source path and returns measured BPM, beat times, and strong onset times as measurement data. Reads are unrestricted; nonexistent paths return media_not_found.")]
    public ValueTask<ToolResult<AnalyzeAudioRhythmResponse>> AnalyzeAudioRhythm(
        [Description("Absolute path of a readable audio file.")]
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
                ToolPaths.RequireAbsolute(path, nameof(path)),
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
    [Description("Exports the current scene through a registered headless encoder to an absolute output path. Omitted frame and audio sample rates use the project settings (30 fps / 44100 Hz without a project rate). Control size/quality with crf or bitrate. If AVFoundation is selected, a requested crf is ignored and reported in the successful result warnings. Pass background:true to run as a job and poll read_render_job(jobId).")]
    public ValueTask<ToolResult<ExportVideoResult>> ExportVideo(
        [Description("Absolute output path. Render outputs use outputPath/outputDirectory; project file tools use path. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Frame-rate numerator. Defaults to the project frame rate, or 30 without a project rate.")]
        int? frameRateNumerator = null,
        [Description("Frame-rate denominator.")]
        int frameRateDenominator = 1,
        [Description("Audio sample rate. Defaults to the project sample rate, or 44100 without a project rate.")]
        int? sampleRate = null,
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
            string resolvedPath = ToolPaths.ResolveForWrite(outputPath, nameof(outputPath));

            // Only preflight-reject when FFmpeg is the sole encoder for this container; a non-FFmpeg
            // encoder (e.g. AVFoundation for macOS .mp4/.mov) can export without the worker.
            if (videoExporter.RequiresFFmpegWorker(resolvedPath)
                && !FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.CodecUnavailable,
                    "export_video requires the Beutl.FFmpegWorker next to this MCP host, but it was not found under the server directory.",
                    "exportVideo",
                    "The stdio MCP host (source-run or standalone install) does not place the FFmpeg worker next to the server. Use the in-app MCP endpoint (Tools > AI Agents) for video export, or run from the installed Beutl app directory where the worker is copied under FFmpegWorker/."));
            }

            Project? project = scene.FindHierarchicalParent<Project>();
            Rational frameRate = frameRateNumerator is { } numerator
                ? new Rational(numerator, frameRateDenominator)
                : project.GetFrameRate();
            int resolvedSampleRate = sampleRate ?? project.GetSampleRate();
            ValidateExportOptions(frameRate, crf, bitrate);

            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);

            async Task<ExportVideoResponse> RunExportAsync(RenderJobProgressReporter progress, CancellationToken token)
            {
                // Re-verify at write time: background jobs are serialized, so a preceding job may
                // have created this file after the pre-flight overwrite check passed.
                destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
                ExportVideoResponse exported = await videoExporter.ExportAsync(
                    scene,
                    resolvedPath,
                    frameRate,
                    resolvedSampleRate,
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
                        JsonSerializerOptions.Web)!,
                    lease));
                return new ExportVideoResult("running", jobId, null);
            }

            ExportVideoResponse response = await RunExportAsync(RenderJobProgressReporter.Ignored, cancellationToken).ConfigureAwait(false);
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

    private static void ValidateExportOptions(Rational frameRate, int? crf, int? bitrate)
    {
        if (frameRate.Numerator <= 0 || frameRate.Denominator <= 0)
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
    }

    private static async ValueTask<CallToolResult> ExecuteMcpAsync<T>(
        Func<ValueTask<(T Value, ImageContentBlock? Image)>> action)
    {
        try
        {
            (T value, ImageContentBlock? image) = await action().ConfigureAwait(false);
            return ToolResult<T>.Success(value).ToCallToolResult(image is null ? null : [image]);
        }
        catch (Exception ex)
        {
            return new ToolResult<T>(default, ToolErrorMapper.Map(ex)).ToCallToolResult();
        }
    }

    private static IReadOnlyList<MediaTime>? NormalizeFrameSampleTimesOrNull(double[]? timeSeconds)
    {
        if (timeSeconds is not { Length: > 0 })
        {
            return null;
        }

        return timeSeconds
            .Where(double.IsFinite)
            .Select(seconds => MediaTime.FromSeconds(Math.Max(0, seconds)))
            .Distinct()
            .OrderBy(time => time)
            .ToArray();
    }

    internal static IReadOnlyList<MediaTime> ResolveFrameSampleTimes(Scene scene, double[]? timeSeconds, int sampleCount)
    {
        IReadOnlyList<MediaTime>? explicitTimes = NormalizeFrameSampleTimesOrNull(timeSeconds);
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
            MediaTime duration = GetEffectiveSceneDuration(scene);
            // TimeRange.Contains excludes the end, so clamp to the last renderable flick rather than the
            // exclusive Duration, which would sample a blank frame with no active elements.
            MediaTime lastRenderable = duration - new MediaTime(1);
            MediaTime[] clamped = explicitTimes
                .Select(time => time > lastRenderable ? lastRenderable : time)
                .Distinct()
                .OrderBy(time => time)
                .ToArray();

            // Multiple out-of-range times can all clamp to the last flick and collapse to one sample,
            // which FrameDifferenceAnalyzer would reject with an unmapped ArgumentException; surface a
            // typed validation error instead of leaking it.
            if (clamped.Length < 2)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "timeSeconds must contain at least two distinct times within the scene duration.",
                    "timeSeconds",
                    $"Provide two or more sample times inside [0, {duration.ToSeconds():0.###}] seconds."));
            }

            return clamped;
        }

        int count = Math.Clamp(sampleCount, 2, 8);
        double durationSeconds = GetEffectiveSceneDuration(scene).ToSeconds();
        return Enumerable
            .Range(0, count)
            .Select(index => MediaTime.FromSeconds(durationSeconds * (index + 0.5) / count))
            .ToArray();
    }

    private static MediaTime GetEffectiveSceneDuration(Scene scene)
        => scene.Duration > MediaTime.Zero ? scene.Duration : MediaTime.FromSeconds(1);

    private OwnedOutputOperation BeginOutputOperation()
    {
        IDisposable lease = outputOperations.TryBeginOutputOperation()
                            ?? throw new OutputOperationBusyException();
        return new OwnedOutputOperation(lease);
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

        // The hint is in thousandths strictly below the exact limit, so it is up to 0.001 lower and a retry with it
        // passes despite float rounding. A frame side over 1000 times the limit leaves no such scale.
        long maximumScaleThousandths = (1000L * maxDimension - 1) / Math.Max(frameSize.Width, frameSize.Height);
        string maximumScaleText = (maximumScaleThousandths / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        string requestedExtent = $"{FormatDeviceExtent(requestedWidth)}x{FormatDeviceExtent(requestedHeight)}";
        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"{toolName} renderScale {normalizedScale.ToString("G9", CultureInfo.InvariantCulture)} requests an output extent of {requestedExtent} pixels. "
            + $"Each output axis is limited to {maxDimension} pixels; "
            + (maximumScaleThousandths > 0
                ? $"the maximum usable renderScale for frame {frameSize.Width}x{frameSize.Height} is {maximumScaleText}."
                : $"frame {frameSize.Width}x{frameSize.Height} needs a renderScale below 0.001."),
            "renderScale",
            maximumScaleThousandths > 0
                ? $"Use renderScale <= {maximumScaleText} for this frame size."
                : "Use a renderScale below 0.001 for this frame size."));
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

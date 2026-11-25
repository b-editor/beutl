using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed record AiModelSummary(
    string Id,
    string Label,
    bool IsDefault,
    bool IsAvailable,
    IReadOnlyList<string>? AspectRatios,
    IReadOnlyList<string>? Backgrounds,
    int? MaxReferenceImages,
    IReadOnlyList<int>? DurationsSeconds,
    IReadOnlyList<string>? Resolutions,
    bool? SupportsAudio,
    bool? SupportsFirstFrame,
    bool? SupportsLastFrame,
    bool? SupportsSeed,
    bool? SupportsPromptToVideo);

public sealed record ListAiJobsResponse(IReadOnlyList<AgentAiJobSnapshot> Jobs)
{
    public string Usage =>
        "Jobs this app session still holds, newest first: running ones and the 128 most recently finished. If a call dropped before returning its jobId, retry with its original requestKey or use this list to find the job.";
}

public sealed record ListAiModelsResponse(string Operation, IReadOnlyList<AiModelSummary> Models)
{
    public string Usage =>
        "Pass a model id to the matching tool, or omit it for the service's default. Lists show the choices each model accepts; a null list means the model publishes none and the tool's default is used.";
}

/// <summary>
/// AI generation for agents, in the in-app host only: it runs on the signed-in account and the
/// same executor as the AI nodes, and saves results next to the scene the call names by sceneId,
/// or the selected tab's scene without one. New jobs are paid. Input
/// files are named by absolute path and uploaded to the service.
/// </summary>
[McpServerToolType]
internal sealed class AgentHostAiTools(
    EditorService editorService,
    AgentAiJobManager jobs,
    IAgentAiBackend backend,
    AgentSessionManager sessions) : ToolBase
{
    internal const string TranscriptionOperation = "audio.transcribe";
    private const int MaxWaitSeconds = 110;

    private const string WaitDescription =
        "Seconds to wait for the result before returning (0-110, default 45). A job still running returns status Running with its jobId; call read_ai_job to wait again.";

    private const string RequestKeyDescription =
        "Optional key of 1-128 characters (for example a UUID). Choose one before the first call, reuse it and the same arguments to recover the original job in this app session, and use a new key for a new result. Keys are scoped to the account and operation.";

    private static readonly string[] s_imageTasks = ["remove_background", "upscale", "restyle", "remove_object", "outpaint"];

    private static readonly string[] s_operations =
    [
        "image.generate", "image.edit.remove_background", "image.edit.upscale", "image.edit.restyle",
        "image.edit.remove_object", "image.edit.outpaint", "video.generate", "video.edit", "video.extend",
        TranscriptionOperation,
    ];

    private static readonly int[] s_outpaintPercents = [10, 25, 50];

    [McpServerTool(Name = "list_ai_models")]
    [Description("Lists the AI models the signed-in account can use for one operation, with the aspect ratios, durations, resolutions and inputs each accepts. Operations: image.generate, image.edit.remove_background, image.edit.upscale, image.edit.restyle, image.edit.remove_object, image.edit.outpaint, video.generate, video.edit, video.extend, audio.transcribe.")]
    public ValueTask<ToolResult<ListAiModelsResponse>> ListAiModels(
        [Description("The operation id, such as video.generate.")]
        string operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<ListAiModelsResponse>(async () =>
        {
            string operationId = operation?.Trim() ?? string.Empty;
            if (!s_operations.Contains(operationId, StringComparer.Ordinal))
                throw Invalid($"Unknown operation '{operation}'. Use one of: {string.Join(", ", s_operations)}.", "operation");
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<GenerativeModelInfo> models =
                await backend.GetModelsAsync(operationId, cancellationToken).ConfigureAwait(false);
            return new ListAiModelsResponse(operationId, models.Select(model => Summarize(model, operationId)).ToArray());
        });
    }

    [McpServerTool(Name = "generate_image")]
    [Description("Generates a picture from a prompt with the signed-in account's AI credits and saves it as a PNG next to the scene sceneId names (the selected tab's scene when sceneId is omitted). Returns the file path when done; place it with apply_edit.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> GenerateImage(
        [Description("What to draw.")]
        string prompt,
        [Description("Aspect ratio such as 16:9 or 1:1. Defaults to the one the model offers nearest the scene.")]
        string? aspectRatio = null,
        [Description("Background: auto, opaque or transparent. Defaults to auto, or the model's first when it offers no auto.")]
        string? background = null,
        [Description("Model id from list_ai_models(image.generate); omit for the default.")]
        string? model = null,
        [Description("Seed for a reproducible result, when the model supports one.")]
        int? seed = null,
        [Description("Absolute paths of pictures to guide the result.")]
        string[]? referenceImagePaths = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        [Description(RequestKeyDescription)]
        string? requestKey = null,
        CancellationToken cancellationToken = default)
    {
        return StartAsync("image.generate", model, waitSeconds, requestKey, cancellationToken, (scene, chosen, requestKeySeed) =>
        {
            string text = RequirePrompt(prompt);
            RequireSeed(seed, chosen?.Image?.SupportsSeed, chosen?.Id);
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(
                    Offered(chosen?.Image?.AspectRatios, GenerativeImageCapabilities.DefaultAspectRatios), scene.FrameSize.Value, "16:9")
                : aspectRatio.Trim();
            string[] referencePaths = referenceImagePaths ?? [];
            // Not read past what any model takes; the executor holds them to the chosen model's limit.
            if (referencePaths.Length > AiRequestLimits.MaxImageReferences)
                throw Invalid($"At most {AiRequestLimits.MaxImageReferences} reference pictures are accepted.", "referenceImagePaths");
            GenerativeImageInput[] references = referencePaths
                .Select((path, index) => ReadImage(path, $"reference-{index + 1}", $"referenceImagePaths[{index}]", AiRequestLimits.MaxImageUploadBytes))
                .ToArray();
            IReadOnlyList<string> backgrounds = Offered(chosen?.Image?.Backgrounds, GenerativeImageCapabilities.DefaultBackgrounds);
            string chosenBackground = string.IsNullOrWhiteSpace(background)
                ? backgrounds.Contains("auto", StringComparer.Ordinal) ? "auto" : backgrounds[0]
                : background.Trim();

            return new AiImageGenerationNodeRequest("image.generate")
            {
                Prompt = text,
                AspectRatio = ratio,
                Background = chosenBackground,
                Seed = seed,
                References = references,
                ModelId = NormalizeModel(model),
                RequestKeySeed = requestKeySeed,
                ParameterFingerprint = GenerativeFingerprint.Combine([text, ratio, chosenBackground, model, seed?.ToString(CultureInfo.InvariantCulture)]),
            };
        });
    }

    [McpServerTool(Name = "edit_image")]
    [Description("Edits a picture with AI credits and saves the result as a PNG next to the scene sceneId names (the selected tab's scene when sceneId is omitted): remove_background, upscale, restyle (prompt required), remove_object (prompt names what to remove) or outpaint (prompt describes the extension).")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> EditImage(
        [Description("Absolute path of the picture to edit.")]
        string sourcePath,
        [Description("remove_background, upscale, restyle, remove_object or outpaint.")]
        string task,
        [Description("Required for restyle, remove_object and outpaint.")]
        string? prompt = null,
        [Description("For outpaint only: how much to add on every side, in percent (10, 25 or 50). Default 25; refused for other tasks.")]
        int? outpaintExpansionPercent = null,
        [Description("Model id from list_ai_models(image.edit.<task>); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        [Description(RequestKeyDescription)]
        string? requestKey = null,
        CancellationToken cancellationToken = default)
    {
        string taskId = task.Trim().ToLowerInvariant();
        int outpaint = outpaintExpansionPercent ?? 25;
        bool requiresPrompt = false;
        return StartAsync($"image.edit.{taskId}", model, waitSeconds, requestKey, cancellationToken, (_, _, requestKeySeed) =>
        {
            var editTask = (AiImageEditTask)Array.IndexOf(s_imageTasks, taskId);
            string? text = requiresPrompt ? RequirePrompt(prompt) : null;
            GenerativeImageInput image = ReadImage(sourcePath, "source", "sourcePath", AiRequestLimits.MaxImageUploadBytes);
            return new AiImageEditNodeRequest($"image.edit.{taskId}")
            {
                Task = editTask,
                Prompt = text,
                OutpaintExpansionPercent = editTask == AiImageEditTask.Outpaint ? outpaint : null,
                Image = image,
                ModelId = NormalizeModel(model),
                RequestKeySeed = requestKeySeed,
                ParameterFingerprint = GenerativeFingerprint.Combine([taskId, text, model, editTask == AiImageEditTask.Outpaint ? outpaint.ToString(CultureInfo.InvariantCulture) : null]),
            };
        },
        // Checked before the model is looked up, since the task names the operation.
        precheck: async token =>
        {
            int index = Array.IndexOf(s_imageTasks, taskId);
            if (index < 0)
                throw Invalid($"Unknown edit task '{task}'. Use one of: {string.Join(", ", s_imageTasks)}.", "task");
            requiresPrompt = await backend.RequiresPromptAsync(taskId, token).ConfigureAwait(false);
            // The executor drops a prompt these tasks do not take; refused instead of charged for.
            if (!requiresPrompt && !string.IsNullOrWhiteSpace(prompt))
                throw Invalid($"The task {taskId} takes no prompt.", "prompt");
            if (taskId == "outpaint" && !s_outpaintPercents.Contains(outpaint))
                throw Invalid("outpaintExpansionPercent must be 10, 25 or 50.", "outpaintExpansionPercent");
            // Only an outpaint widens the canvas; another task would drop the value and charge anyway.
            if (taskId != "outpaint" && outpaintExpansionPercent is not null)
                throw Invalid($"outpaintExpansionPercent is only for the outpaint task, not {taskId}.", "outpaintExpansionPercent");
        });
    }

    [McpServerTool(Name = "generate_video")]
    [Description("Generates a video clip from a prompt, optionally starting on and ending at given pictures, and saves it next to the scene sceneId names (the selected tab's scene when sceneId is omitted). Takes minutes: returns a running job unless it finishes within waitSeconds. Longer and higher-resolution clips cost more AI credits.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> GenerateVideo(
        [Description("What happens in the clip.")]
        string prompt,
        [Description("Length in seconds; must be one the model offers (see list_ai_models(video.generate)). Defaults to 6, or the model's first when it offers no 6.")]
        int? durationSeconds = null,
        [Description("Resolution such as 720p or 1080p. Defaults to the smallest the model offers that covers the scene.")]
        string? resolution = null,
        [Description("Aspect ratio such as 16:9 or 9:16. Defaults to the one the model offers nearest the scene.")]
        string? aspectRatio = null,
        [Description("Generate sound with the picture; refused for a model whose supportsAudio is false.")]
        bool generateAudio = false,
        [Description("Absolute path of a picture the clip starts on.")]
        string? firstFramePath = null,
        [Description("Absolute path of a picture the clip ends on; needs firstFramePath.")]
        string? lastFramePath = null,
        [Description("Model id from list_ai_models(video.generate); omit for the default.")]
        string? model = null,
        [Description("Seed for a reproducible result, when the model supports one.")]
        int? seed = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        [Description(RequestKeyDescription)]
        string? requestKey = null,
        CancellationToken cancellationToken = default)
    {
        return StartAsync("video.generate", model, waitSeconds, requestKey, cancellationToken, (scene, chosen, requestKeySeed) =>
        {
            string text = RequirePrompt(prompt);
            RequireSeed(seed, chosen?.Video?.SupportsSeed, chosen?.Id);
            if (lastFramePath is not null && firstFramePath is null)
                throw Invalid("A last frame needs a first frame.", "lastFramePath");
            GenerativeVideoCapabilities? offered = chosen?.Video;
            // The executor would quietly drop the sound; refused instead, so a silent clip is not paid for.
            if (generateAudio && offered is { SupportsAudio: false })
                throw Invalid($"The model '{chosen!.Id}' does not generate sound. Leave generateAudio off, or choose a model whose supportsAudio is true.", "generateAudio");
            int duration = durationSeconds ?? DefaultDuration(offered);
            string chosenResolution = string.IsNullOrWhiteSpace(resolution)
                ? GenerativeShapeSuggestion.SuggestResolution(
                    Offered(offered?.Resolutions, GenerativeVideoCapabilities.DefaultResolutions), scene.FrameSize.Value)
                : resolution.Trim();
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(
                    Offered(offered?.AspectRatios, GenerativeVideoCapabilities.DefaultAspectRatios), scene.FrameSize.Value, "16:9")
                : aspectRatio.Trim();

            return new AiVideoGenerationNodeRequest("video.generate")
            {
                Prompt = text,
                DurationSeconds = duration,
                Resolution = chosenResolution,
                AspectRatio = ratio,
                GenerateAudio = generateAudio,
                Seed = seed,
                FirstFrame = firstFramePath is null ? null : ReadImage(firstFramePath, "first-frame", "firstFramePath", AiRequestLimits.MaxFrameUploadBytes),
                LastFrame = lastFramePath is null ? null : ReadImage(lastFramePath, "last-frame", "lastFramePath", AiRequestLimits.MaxFrameUploadBytes),
                ModelId = NormalizeModel(model),
                RequestKeySeed = requestKeySeed,
                ParameterFingerprint = GenerativeFingerprint.Combine(
                    [text, duration.ToString(CultureInfo.InvariantCulture), chosenResolution, ratio, generateAudio ? "audio" : null, model, seed?.ToString(CultureInfo.InvariantCulture)]),
            };
        });
    }

    [McpServerTool(Name = "edit_video")]
    [Description("Uses AI credits to remake a clip (mode edit) or continue it (mode extend), and saves the result next to the scene sceneId names (the selected tab's scene when sceneId is omitted). An extension comes back as the whole clip with the new part added at the end. Accepts mp4 or webm up to 32 MB. Takes minutes.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> EditVideo(
        [Description("Absolute path of the clip to edit or extend.")]
        string sourcePath,
        [Description("What to change, or what happens next.")]
        string prompt,
        [Description("edit or extend.")]
        string mode = "edit",
        [Description("For extend: seconds to add; must be one the model offers (see list_ai_models(video.extend)). Defaults to 6, or the model's first when it offers no 6.")]
        int? durationSeconds = null,
        [Description("Model id from list_ai_models(video.edit or video.extend); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        [Description(RequestKeyDescription)]
        string? requestKey = null,
        CancellationToken cancellationToken = default)
    {
        string modeId = mode.Trim().ToLowerInvariant();
        return StartAsync(modeId == "extend" ? "video.extend" : "video.edit", model, waitSeconds, requestKey, cancellationToken, (_, chosen, requestKeySeed) =>
        {
            bool extend = modeId == "extend";
            string text = RequirePrompt(prompt);
            int duration = extend ? durationSeconds ?? DefaultDuration(chosen?.Video) : 0;
            GenerativeFileInput source = ReadVideo(sourcePath);
            return new AiVideoEditNodeRequest(extend ? "video.extend" : "video.edit")
            {
                Mode = extend ? AiVideoEditMode.Extend : AiVideoEditMode.Edit,
                Prompt = text,
                DurationSeconds = duration,
                SourceVideo = source,
                ModelId = NormalizeModel(model),
                RequestKeySeed = requestKeySeed,
                ParameterFingerprint = GenerativeFingerprint.Combine([modeId, text, extend ? duration.ToString(CultureInfo.InvariantCulture) : null, model]),
            };
        },
        precheck: _ =>
        {
            if (modeId is not ("edit" or "extend"))
                throw Invalid($"Unknown mode '{mode}'. Use edit or extend.", "mode");
            if (modeId == "edit" && durationSeconds is not null)
                throw Invalid("An edit keeps the clip's own length; durationSeconds is only for mode extend.", "durationSeconds");
            return ValueTask.CompletedTask;
        });
    }

    [McpServerTool(Name = "transcribe_audio")]
    [Description("Transcribes the speech in an audio or video file and returns timed segments, and words when the model reports them. Long files are sent in ten-minute parts. Paid by length.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> TranscribeAudio(
        [Description("Absolute path of the audio or video file.")]
        string sourcePath,
        [Description("Language code such as ja or en; omit to detect it.")]
        string? language = null,
        [Description("Model id from list_ai_models(audio.transcribe); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        [Description(RequestKeyDescription + " For a failed or cancelled transcription, the same key resumes from saved progress, including after restarting Beutl.")]
        string? requestKey = null,
        CancellationToken cancellationToken = default)
    {
        return StartAccountJobAsync(TranscriptionOperation, waitSeconds, requestKey, cancellationToken, async (account, token) =>
        {
            string? languageCode = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
            if (languageCode is not null && !AiRequestLimits.IsIso6391LanguageCode(languageCode))
                throw Invalid($"'{language}' is not a two-letter ISO 639-1 language code such as ja or en.", "language");
            string? modelId = NormalizeModel(model);
            AgentTranscriptionRetry? retry = requestKey is null ? null : new(requestKey, modelId);
            bool recovering = retry is { } saved && account.AccountId is { } accountId
                && backend.RecoveryContext is { } recovery
                && AgentTranscriptionRecovery.Exists(recovery.Store.StoragePath, accountId, saved.Key);
            if (!recovering)
            {
                using (account.Enter(token))
                    await RequireAvailableAsync(token).ConfigureAwait(false);
                GenerativeModelInfo? chosen = await account.SendAsync(
                    ct => ResolveModelAsync(TranscriptionOperation, model, ct, sendsNamedModel: true), token).ConfigureAwait(false);
                modelId = chosen?.Id ?? modelId;
            }
            // On recovery the backend uses the saved model and only checks credits for unsent parts.
            string path = RequireFile(sourcePath, "sourcePath");
            return new PreparedAiJob(async (progress, jobToken) =>
            {
                AgentTranscriptionResult result = await backend
                    .TranscribeAsync(path, languageCode, modelId, progress, jobToken, retry)
                    .ConfigureAwait(false);
                return new AgentAiJobOutput(null, "transcript", result.ModelId, null, result.Transcript);
            });
        });
    }

    [McpServerTool(Name = "list_ai_jobs")]
    [Description("Lists the AI jobs this app session holds, newest first, with their status and results. A job keeps running when the call that started it is cancelled or its connection drops. Use this list to recover the job if the original call had no requestKey.")]
    public ToolResult<ListAiJobsResponse> ListAiJobs()
        => Execute(() => new ListAiJobsResponse(jobs.List()));

    [McpServerTool(Name = "read_ai_job")]
    [Description("Reports an AI job started by generate_image, edit_image, generate_video, edit_video or transcribe_audio, waiting up to waitSeconds for it to finish. A finished job carries the saved file path or the transcript.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> ReadAiJob(
        [Description("The jobId an AI tool returned.")]
        string jobId,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() =>
        {
            RequireWait(waitSeconds);
            return new ValueTask<AgentAiJobSnapshot>(WaitAsync(jobId, waitSeconds, cancellationToken));
        });
    }

    [McpServerTool(Name = "cancel_ai_job")]
    [Description("Cancels an AI job in the app: its work stops and the job ends Cancelled with no result here. A request the service has already accepted is still charged, and its result can be collected from the AI tab's job history in the app.")]
    public ToolResult<AgentAiJobSnapshot> CancelAiJob(
        [Description("The jobId to cancel.")]
        string jobId)
    {
        return Execute(() =>
        {
            if (!jobs.Cancel(jobId))
                throw NotFound(jobId);
            return jobs.Get(jobId)!;
        });
    }

    private ValueTask<ToolResult<AgentAiJobSnapshot>> StartAsync(
        string operation,
        string? model,
        int waitSeconds,
        string? requestKey,
        CancellationToken cancellationToken,
        Func<Scene, GenerativeModelInfo?, string, GenerativeRequest> build,
        Func<CancellationToken, ValueTask>? precheck = null)
    {
        return StartAccountJobAsync(operation, waitSeconds, requestKey, cancellationToken, async (account, token) =>
        {
            if (precheck is not null)
            {
                using (account.Enter(token))
                    await precheck(token).ConfigureAwait(false);
            }
            using (account.Enter(token))
                await RequireAvailableAsync(token).ConfigureAwait(false);
            Scene scene = await RequireSceneAsync().ConfigureAwait(false);
            // Omitted settings are chosen from what this model offers, as the AI tab's controls are.
            GenerativeModelInfo? chosen = await account.SendAsync(
                ct => ResolveModelAsync(operation, model, ct), token).ConfigureAwait(false);
            // Inputs are read now, so a missing file or a bad argument is the call's error, not the job's.
            GenerativeRequest request;
            try
            {
                string seed = requestKey is null
                    ? AiRequestKey.NewSeed()
                    : GenerativeFingerprint.Combine([account.AccountId, operation, requestKey])[..32];
                request = build(scene, chosen, seed);
                if (requestKey is not null)
                    request = request with { HasCallerRequestKey = true };
            }
            catch (GenerativeExecutionException ex)
            {
                throw Invalid(ex.Message, null);
            }

            IGenerativeNodeExecutor executor = backend.CreateExecutor(scene);
            // The executor's own checks, so a request it would refuse is the call's error, not a failed job.
            if (await account.SendAsync(ct => executor.ValidateAsync(request, ct), token).ConfigureAwait(false) is { } refusal)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    refusal.Message,
                    ToArgument(refusal.Input),
                    $"Call list_ai_models(\"{operation}\") for what each model takes."));
            }

            return new PreparedAiJob(async (progress, jobToken) =>
            {
                GenerativeExecutionResult result;
                try
                {
                    result = await executor
                        .ExecuteAsync(request, new StatusProgress(progress), jobToken)
                        .ConfigureAwait(false);
                }
                catch (GenerativeExecutionException ex)
                {
                    // The executor keeps the service's refusal as the inner exception.
                    string code = ex.Input == nameof(GenerativeRequest.RequestKeySeed)
                        ? ErrorCode.ValidationRejected
                        : AgentAiException.CodeFor(ex.InnerException);
                    throw new AgentAiException(code, ex.Message);
                }

                return new AgentAiJobOutput(
                    result.ResultFile.LocalPath,
                    result.IsVideo ? "video" : "image",
                    result.ModelId,
                    result.Seed);
            }, scene.Id.Value);
        });
    }

    private ValueTask<ToolResult<AgentAiJobSnapshot>> StartAccountJobAsync(
        string operation,
        int waitSeconds,
        string? requestKey,
        CancellationToken cancellationToken,
        Func<AiAccountJobScope, CancellationToken, Task<PreparedAiJob>> prepare)
    {
        return ExecuteAsync<AgentAiJobSnapshot>(async () =>
        {
            RequireWait(waitSeconds);
            if (requestKey is not null && (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 128))
                throw Invalid("requestKey must contain 1 to 128 characters and cannot be blank.", "requestKey");

            AiAccountJobScope? account = null;
            bool queued = false;
            try
            {
                var job = account = new AiAccountJobScope(backend.RecoveryContext);
                AgentAiRequestKey? key = requestKey is null ? null : new(job.AccountId, operation, requestKey);
                PreparedAiJob prepared;
                using (CancellationTokenSource preflight = job.Link(cancellationToken))
                {
                    job.ThrowIfInvalid(preflight.Token);
                    if (key is { } replayKey && jobs.FindReplay(replayKey) is { } existingId)
                    {
                        AgentAiJobSnapshot existing = await WaitAsync(existingId, waitSeconds, preflight.Token).ConfigureAwait(false);
                        job.ThrowIfInvalid(preflight.Token);
                        return existing;
                    }

                    prepared = await prepare(job, preflight.Token).ConfigureAwait(false);
                    job.ThrowIfInvalid(preflight.Token);
                }

                // The queued job owns the account guard. Stopping the MCP wait is independent of the job.
                string jobId;
                (jobId, queued) = jobs.GetOrStart(operation, (progress, token) => RunAccountJobAsync(job, prepared, progress, token),
                    prepared.SceneId, key);
                if (queued)
                    return await WaitAsync(jobId, waitSeconds, cancellationToken).ConfigureAwait(false);

                // Another call queued the same request while this one was preparing it.
                using CancellationTokenSource replay = job.Link(cancellationToken);
                AgentAiJobSnapshot result = await WaitAsync(jobId, waitSeconds, replay.Token).ConfigureAwait(false);
                job.ThrowIfInvalid(replay.Token);
                return result;
            }
            catch (Exception ex) when (!queued && (ex is AuthenticationRequiredException || account?.AccountChanged == true))
            {
                throw Unavailable(AgentHostAiBackend.SignInReason);
            }
            finally
            {
                if (!queued)
                    account?.Dispose();
            }
        });
    }

    private static async Task<AgentAiJobOutput> RunAccountJobAsync(
        AiAccountJobScope account,
        PreparedAiJob prepared,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        using (account)
        using (CancellationTokenSource cancellation = account.Link(cancellationToken))
        {
            try
            {
                using IDisposable scope = account.Enter(cancellation.Token);
                AgentAiJobOutput output = await prepared.Run(progress, cancellation.Token).ConfigureAwait(false);
                account.ThrowIfInvalid(cancellation.Token);
                return output;
            }
            catch (Exception ex) when (ex is AuthenticationRequiredException || account.AccountChanged)
            {
                throw AgentAiException.AuthenticationRequired();
            }
        }
    }

    private sealed record PreparedAiJob(
        Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> Run,
        Guid? SceneId = null);

    private async Task<AgentAiJobSnapshot> WaitAsync(string jobId, int waitSeconds, CancellationToken cancellationToken)
    {
        return await jobs.WaitAsync(jobId, TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false)
               ?? throw NotFound(jobId);
    }

    // Checked before anything starts: a paid job is never begun for a call that is then refused.
    private static void RequireWait(int waitSeconds)
    {
        if (waitSeconds is < 0 or > MaxWaitSeconds)
            throw Invalid($"waitSeconds must be between 0 and {MaxWaitSeconds}.", "waitSeconds");
    }

    // As the executor resolves it: the named model, or the default the account can use. A named model an
    // empty (offline) catalog cannot confirm is refused before a job starts, unless the caller sends the id.
    private async Task<GenerativeModelInfo?> ResolveModelAsync(
        string operation,
        string? model,
        CancellationToken cancellationToken,
        bool sendsNamedModel = false)
    {
        IReadOnlyList<GenerativeModelInfo> offered =
            await backend.GetModelsAsync(operation, cancellationToken).ConfigureAwait(false);
        if (NormalizeModel(model) is { } id)
        {
            GenerativeModelInfo? named = offered.FirstOrDefault(candidate => candidate.Id == id);
            // While the plan cannot be read every model reads unavailable. A request that sends the
            // id itself lets the service decide; the executor refuses an unavailable model, so a
            // generation is still refused here.
            bool refused = named is null
                || (!named.IsAvailable && (backend.KnowsModelAvailability || !sendsNamedModel));
            if (offered.Count > 0 && refused)
                throw Invalid($"The model '{id}' is not available for {operation}. Call list_ai_models(\"{operation}\") for the ones this account can use.", "model");
            if (named is null && !sendsNamedModel)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"The model '{id}' cannot be confirmed because no model list is available for {operation}.",
                    "model",
                    "Retry once list_ai_models lists it, or omit model to use the service default."));
            }

            return named;
        }

        GenerativeModelInfo? fallback = offered.FirstOrDefault(candidate => candidate.IsAvailable && candidate.IsDefault)
                                       ?? offered.FirstOrDefault(candidate => candidate.IsAvailable);
        // A catalog that lists models but none this account can use would otherwise send the
        // request with no model, to a default the catalog says is unavailable. While the plan cannot
        // be read every model reads unavailable, so the service decides instead.
        if (fallback is null && offered.Count > 0 && backend.KnowsModelAvailability)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.AiUnavailable,
                $"None of the models for {operation} is available to this account.",
                "model",
                $"Call list_ai_models(\"{operation}\") to see each model's isAvailable; the account's plan may not include them."));
        }

        return fallback;
    }

    // The tool argument behind the input of a request the executor refuses.
    private static string? ToArgument(string? input) => input switch
    {
        nameof(AiImageGenerationNodeRequest.Prompt) => "prompt",
        nameof(AiImageGenerationNodeRequest.ModelId) => "model",
        nameof(AiImageGenerationNodeRequest.AspectRatio) => "aspectRatio",
        nameof(AiImageGenerationNodeRequest.Background) => "background",
        nameof(AiImageGenerationNodeRequest.Seed) => "seed",
        nameof(AiImageGenerationNodeRequest.References) => "referenceImagePaths",
        nameof(AiImageEditNodeRequest.Image) or nameof(AiVideoEditNodeRequest.SourceVideo) => "sourcePath",
        nameof(AiImageEditNodeRequest.OutpaintExpansionPercent) => "outpaintExpansionPercent",
        nameof(AiVideoGenerationNodeRequest.DurationSeconds) => "durationSeconds",
        nameof(AiVideoGenerationNodeRequest.Resolution) => "resolution",
        nameof(AiVideoGenerationNodeRequest.FirstFrame) => "firstFramePath",
        nameof(AiVideoGenerationNodeRequest.LastFrame) => "lastFramePath",
        _ => null,
    };

    private static IReadOnlyList<T> Offered<T>(IReadOnlyList<T>? published, IReadOnlyList<T> fallback)
        => published is { Count: > 0 } ? published : fallback;

    private static int DefaultDuration(GenerativeVideoCapabilities? offered)
        => GenerativeShapeSuggestion.SuggestDuration(
            Offered(offered?.DurationsSeconds, GenerativeVideoCapabilities.DefaultDurations),
            null,
            GenerativeVideoCapabilities.DefaultDuration);

    private async Task RequireAvailableAsync(CancellationToken cancellationToken)
    {
        if (await backend.GetUnavailableReasonAsync(cancellationToken).ConfigureAwait(false) is { } reason)
            throw Unavailable(reason);
    }

    private static ReconcileException Unavailable(string reason)
        => new(new ToolError(
            ErrorCode.AiUnavailable,
            reason,
            null,
            "AI tools run on the account signed in to the Beutl app; sign in there, then call the tool again."));

    private Task<Scene?> FindSceneAsync()
        => Dispatcher.UIThread.InvokeAsync(
            () => (editorService.SelectedTabItem.Value?.Context.Value as EditViewModel)?.Scene).GetTask();

    // Results are kept with the scene, so they are saved and moved with the project. A sceneId names
    // the scene as on the scene tools: the scene router resolves it for this request. Only a call
    // without one falls back to the selected tab.
    private async Task<Scene> RequireSceneAsync()
    {
        if (sessions.CurrentSession is { } session && session.ReadOnSession(() => session.Root) is Scene target)
            return target;

        Scene? scene = await FindSceneAsync().ConfigureAwait(false);
        return scene ?? throw new ReconcileException(new ToolError(
            ErrorCode.NoActiveEditorSession,
            "AI results are saved next to a scene. No sceneId was given, and no scene is open in the editor.",
            null,
            "Pass sceneId from list_scenes, or open or create a project in the Beutl editor (or call create_project/open_project), then call the tool again."));
    }

    // The catalog fills image capabilities for every model, video ones included, so only the
    // operation's own kind is reported; otherwise a video model shows the image fallback's seed.
    private static AiModelSummary Summarize(GenerativeModelInfo model, string operation)
    {
        GenerativeImageCapabilities? image = operation.StartsWith("image.", StringComparison.Ordinal) ? model.Image : null;
        GenerativeVideoCapabilities? video = operation.StartsWith("video.", StringComparison.Ordinal) ? model.Video : null;
        return new(
            model.Id,
            model.Label,
            model.IsDefault,
            model.IsAvailable,
            image?.AspectRatios ?? video?.AspectRatios,
            image?.Backgrounds,
            image?.MaxReferenceImages,
            video?.DurationsSeconds,
            video?.Resolutions,
            video?.SupportsAudio,
            video?.SupportsFirstFrame,
            video?.SupportsLastFrame,
            image?.SupportsSeed ?? video?.SupportsSeed,
            // False when generate_video needs firstFramePath for this model.
            video?.SupportsPromptToVideo);
    }

    private static string? NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    private static string RequirePrompt(string? prompt)
        => string.IsNullOrWhiteSpace(prompt) ? throw Invalid("A prompt is required.", "prompt") : prompt.Trim();

    // The executor drops a seed the model does not take; refused instead, since the caller asked
    // for a result it can reproduce.
    private static void RequireSeed(int? seed, bool? supportsSeed, string? modelId)
    {
        if (seed is not null && supportsSeed == false)
            throw Invalid($"The model '{modelId}' does not take a seed. Omit seed, or choose a model whose supportsSeed is true.", "seed");
    }

    private static string RequireFile(string? path, string target)
    {
        string full = ToolPaths.RequireAbsolute(path, target);
        return File.Exists(full)
            ? full
            : throw new ReconcileException(new ToolError(ErrorCode.MediaNotFound, $"No file exists at '{path}'.", target));
    }

    // name is the upload's file name; target is the tool argument an error points at.
    private static GenerativeImageInput ReadImage(string path, string name, string target, long maxBytes)
    {
        string full = RequireFile(path, target);
        try
        {
            // Size and dimensions are checked before decoding, as the AI dialogs check them, so a
            // small file that would decode to a huge picture is refused unread.
            using Bitmap bitmap = AiImageDecodeValidator.LoadValidatedBitmap(full, maxBytes);
            using var stream = new MemoryStream();
            if (!bitmap.Save(stream, EncodedImageFormat.Png))
                throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", target));
            return new GenerativeImageInput($"{name}.png", stream.ToArray());
        }
        catch (InvalidDataException)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.MediaUnsupported,
                $"'{path}' is not a picture the AI service accepts: it must be a readable image of at most {maxBytes / (1024 * 1024)} MB and {AiImageDecodeValidator.MaxDimension} pixels a side.",
                target));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", target));
        }
    }

    private GenerativeFileInput ReadVideo(string path)
    {
        string full = RequireFile(path, "sourcePath");
        if (!GenerativeInputs.IsSupportedVideoFile(full))
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, "Only mp4 and webm clips can be edited.", "sourcePath"));
        GenerativeFileInput input = GenerativeInputs.ReadVideoFile(full, "source");
        // Opened now, as the executor opens it to read its length, so a file that is not a clip is
        // the call's media_unsupported rather than the executor's refusal. Probed from the bytes that
        // will be uploaded, as the executor probes them: the file on disk may change in between.
        (string probe, FileStream stream) = AiTemporaryFileStore.Create("inputs", "agent-source-video", Path.GetExtension(input.Name));
        try
        {
            // Written outside the decode catch: a full disk is not the clip's fault, and its error
            // takes the logged path for unexpected failures.
            using (stream)
                stream.Write(input.Content);
            if (OpensAsVideo(probe))
                return input;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be opened as a video.", "sourcePath"));
    }

    private bool OpensAsVideo(string path)
    {
        try
        {
            using MediaReader reader = MediaReader.Open(
                path, new MediaOptions(MediaMode.Video) { PreferProxy = false, IsBackground = true }, editorService.AppServices.GetRequiredService<DecoderRegistry>());
            return reader.HasVideo;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static ReconcileException Invalid(string message, string? target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));

    private static ReconcileException NotFound(string jobId)
        => new(new ToolError(ErrorCode.AiJobNotFound, $"No AI job with id '{jobId}' exists in this app session.", jobId));

    // Status text only; the previews the executor reports are of no use to an agent.
    private sealed class StatusProgress(IProgress<string> progress) : IProgress<GenerativeProgress>
    {
        public void Report(GenerativeProgress value)
        {
            value.Preview?.Dispose();
            if (value.Status is { } status)
                progress.Report(status);
        }
    }
}

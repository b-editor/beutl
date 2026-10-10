using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Api.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
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
    bool? SupportsSeed);

public sealed record ListAiModelsResponse(string Operation, IReadOnlyList<AiModelSummary> Models)
{
    public string Usage =>
        "Pass a model id to the matching tool, or omit it for the service's default. Lists show the choices each model accepts; a null list means the model publishes none and the tool's default is used.";
}

/// <summary>
/// AI generation for agents, in the in-app host only: it runs on the signed-in account and the
/// same executor as the AI nodes, and saves results next to the open scene. Generations are paid;
/// each call is one request. Input files are uploaded to the service, so they must come from the
/// workspace or from the open scene's AI results.
/// </summary>
[McpServerToolType]
internal sealed class AgentHostAiTools(
    EditorService editorService,
    AgentAiJobManager jobs,
    IAgentAiBackend backend,
    IWorkspaceGuard workspace) : ToolBase
{
    private const int MaxWaitSeconds = 110;

    private const string WaitDescription =
        "Seconds to wait for the result before returning (0-110, default 45). A job still running returns status Running with its jobId; call read_ai_job to wait again.";

    private static readonly string[] s_imageTasks = ["remove_background", "upscale", "restyle", "remove_object", "outpaint"];

    private static readonly string[] s_operations =
    [
        "image.generate", "image.edit.remove_background", "image.edit.upscale", "image.edit.restyle",
        "image.edit.remove_object", "image.edit.outpaint", "video.generate", "video.edit", "video.extend",
        "audio.transcribe",
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
    [Description("Generates a picture from a prompt with the signed-in account's AI credits and saves it as a PNG next to the open scene. Returns the file path when done; place it with apply_edit. Paid per call.")]
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
        [Description("Paths of pictures to guide the result, in the workspace or among the open scene's AI results.")]
        string[]? referenceImagePaths = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        return StartAsync("image.generate", model, waitSeconds, cancellationToken, (scene, chosen) =>
        {
            string text = RequirePrompt(prompt);
            RequireSeed(seed, chosen?.Image?.SupportsSeed, chosen?.Id);
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(
                    Offered(chosen?.Image?.AspectRatios, GenerativeImageCapabilities.DefaultAspectRatios), scene.FrameSize, "16:9")
                : aspectRatio.Trim();
            string[] referencePaths = referenceImagePaths ?? [];
            int maxReferences = Math.Min(AiRequestLimits.MaxImageReferences, chosen?.Image?.MaxReferenceImages ?? AiRequestLimits.MaxImageReferences);
            if (referencePaths.Length > maxReferences)
                throw Invalid($"At most {maxReferences} reference pictures are accepted.", "referenceImagePaths");
            GenerativeImageInput[] references = referencePaths
                .Select((path, index) => ReadImage(scene, path, $"reference-{index + 1}", AiRequestLimits.MaxImageUploadBytes))
                .ToArray();
            IReadOnlyList<string> backgrounds = Offered(chosen?.Image?.Backgrounds, GenerativeImageCapabilities.DefaultBackgrounds);
            string chosenBackground = string.IsNullOrWhiteSpace(background)
                ? backgrounds.Contains("auto", StringComparer.Ordinal) ? "auto" : backgrounds[0]
                : background.Trim();
            if (chosen?.Image is { } offered)
            {
                RequireOffered(ratio, offered.AspectRatioChoices, chosen.Id, "aspectRatio");
                RequireOffered(chosenBackground, offered.BackgroundChoices, chosen.Id, "background");
            }

            return new AiImageGenerationNodeRequest("image.generate")
            {
                Prompt = text,
                AspectRatio = ratio,
                Background = chosenBackground,
                Seed = seed,
                References = references,
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine([text, ratio, chosenBackground, model, seed?.ToString(CultureInfo.InvariantCulture)]),
            };
        });
    }

    [McpServerTool(Name = "edit_image")]
    [Description("Edits a picture with AI and saves the result as a PNG next to the open scene: remove_background, upscale, restyle (prompt required), remove_object (prompt names what to remove) or outpaint (prompt describes the extension). Paid per call.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> EditImage(
        [Description("Path of the picture to edit, in the workspace or among the open scene's AI results.")]
        string sourcePath,
        [Description("remove_background, upscale, restyle, remove_object or outpaint.")]
        string task,
        [Description("Required for restyle, remove_object and outpaint.")]
        string? prompt = null,
        [Description("For outpaint: how much to add on every side, in percent (10, 25 or 50). Default 25.")]
        int outpaintExpansionPercent = 25,
        [Description("Model id from list_ai_models(image.edit.<task>); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        string taskId = task.Trim().ToLowerInvariant();
        return StartAsync($"image.edit.{taskId}", model, waitSeconds, cancellationToken, (scene, _) =>
        {
            var editTask = (AiImageEditTask)Array.IndexOf(s_imageTasks, taskId);
            // The executor puts its instruction in front of an outpaint's prompt before the same limit.
            int maxPrompt = editTask == AiImageEditTask.Outpaint
                ? AiRequestLimits.MaxPromptLength - AiGenerativeNodeExecutor.OutpaintInstruction.Length - 1
                : AiRequestLimits.MaxPromptLength;
            string? text = editTask.RequiresPrompt() ? RequirePrompt(prompt, maxPrompt) : null;
            GenerativeImageInput image = ReadImage(scene, sourcePath, "source", AiRequestLimits.MaxImageUploadBytes, out PixelSize size);
            if (editTask == AiImageEditTask.Outpaint)
                RequireOutpaintCanvas(size, outpaintExpansionPercent);
            return new AiImageEditNodeRequest($"image.edit.{taskId}")
            {
                Task = editTask,
                Prompt = text,
                OutpaintExpansionPercent = editTask == AiImageEditTask.Outpaint ? outpaintExpansionPercent : null,
                Image = image,
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine([taskId, text, model, outpaintExpansionPercent.ToString(CultureInfo.InvariantCulture)]),
            };
        },
        // Checked before the model is looked up, since the task names the operation.
        precheck: () =>
        {
            if (Array.IndexOf(s_imageTasks, taskId) < 0)
                throw Invalid($"Unknown edit task '{task}'. Use one of: {string.Join(", ", s_imageTasks)}.", "task");
            if (taskId == "outpaint" && !s_outpaintPercents.Contains(outpaintExpansionPercent))
                throw Invalid("outpaintExpansionPercent must be 10, 25 or 50.", "outpaintExpansionPercent");
        });
    }

    [McpServerTool(Name = "generate_video")]
    [Description("Generates a video clip from a prompt, optionally starting on and ending at given pictures, and saves it next to the open scene. Takes minutes: returns a running job unless it finishes within waitSeconds. Paid per call; longer and higher-resolution clips cost more.")]
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
        [Description("Path of a picture the clip starts on, in the workspace or among the open scene's AI results.")]
        string? firstFramePath = null,
        [Description("Path of a picture the clip ends on, in the workspace or among the open scene's AI results; needs firstFramePath.")]
        string? lastFramePath = null,
        [Description("Model id from list_ai_models(video.generate); omit for the default.")]
        string? model = null,
        [Description("Seed for a reproducible result, when the model supports one.")]
        int? seed = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        return StartAsync("video.generate", model, waitSeconds, cancellationToken, (scene, chosen) =>
        {
            string text = RequirePrompt(prompt, MaxPromptLength(chosen?.Video));
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
                    Offered(offered?.Resolutions, GenerativeVideoCapabilities.DefaultResolutions), scene.FrameSize)
                : resolution.Trim();
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(
                    Offered(offered?.AspectRatios, GenerativeVideoCapabilities.DefaultAspectRatios), scene.FrameSize, "16:9")
                : aspectRatio.Trim();
            if (offered is not null)
            {
                string id = chosen!.Id;
                RequireOffered(duration, offered.DurationChoices, id, "durationSeconds");
                RequireOffered(chosenResolution, offered.ResolutionChoices, id, "resolution");
                RequireOffered(ratio, offered.AspectRatioChoices, id, "aspectRatio");
                if (firstFramePath is not null && !offered.SupportsFirstFrame)
                    throw Invalid($"The model '{id}' does not start from a picture.", "firstFramePath");
                if (lastFramePath is not null && !offered.SupportsLastFrame)
                    throw Invalid($"The model '{id}' does not end on a picture.", "lastFramePath");
                if (firstFramePath is null && !offered.SupportsPromptToVideo)
                    throw Invalid($"The model '{id}' needs a picture to start from.", "firstFramePath");
            }

            return new AiVideoGenerationNodeRequest("video.generate")
            {
                Prompt = text,
                DurationSeconds = duration,
                Resolution = chosenResolution,
                AspectRatio = ratio,
                GenerateAudio = generateAudio,
                Seed = seed,
                FirstFrame = firstFramePath is null ? null : ReadImage(scene, firstFramePath, "first-frame", AiRequestLimits.MaxFrameUploadBytes),
                LastFrame = lastFramePath is null ? null : ReadImage(scene, lastFramePath, "last-frame", AiRequestLimits.MaxFrameUploadBytes),
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine(
                    [text, duration.ToString(CultureInfo.InvariantCulture), chosenResolution, ratio, generateAudio ? "audio" : null, model, seed?.ToString(CultureInfo.InvariantCulture)]),
            };
        });
    }

    [McpServerTool(Name = "edit_video")]
    [Description("Remakes a clip from a prompt (mode edit) or continues it (mode extend), and saves the result next to the open scene. An extension comes back as the whole clip with the new part added at the end. Accepts mp4 or webm up to 32 MB. Takes minutes; paid per call.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> EditVideo(
        [Description("Path of the clip to edit or extend, in the workspace or among the open scene's AI results.")]
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
        CancellationToken cancellationToken = default)
    {
        string modeId = mode.Trim().ToLowerInvariant();
        return StartAsync(modeId == "extend" ? "video.extend" : "video.edit", model, waitSeconds, cancellationToken, (scene, chosen) =>
        {
            bool extend = modeId == "extend";
            string text = RequirePrompt(prompt, MaxPromptLength(chosen?.Video));
            int duration = extend ? durationSeconds ?? DefaultDuration(chosen?.Video) : 0;
            if (extend && chosen?.Video is { } offered)
                RequireOffered(duration, offered.DurationChoices, chosen.Id, "durationSeconds");
            (GenerativeFileInput source, double seconds) = ReadVideo(scene, sourcePath);
            // As the executor checks the clip against the model before anything is reserved.
            GenerativeVideoCapabilities limits = chosen?.Video ?? GenerativeVideoCapabilities.Unrestricted;
            if (source.Content.LongLength > limits.MaxSourceVideoBytes)
                throw Invalid($"The clip is larger than the {limits.MaxSourceVideoBytes / (1024 * 1024)} MB the model takes.", "sourcePath");
            double shortest = limits.MinSourceVideoSeconds ?? 0;
            double longest = Math.Min(limits.MaxSourceVideoSeconds ?? 60, 60);
            if (!double.IsFinite(seconds) || seconds <= 0 || seconds < shortest || seconds > longest)
                throw Invalid($"The clip lasts {seconds:0.#} seconds; the model takes clips of {shortest:0.#} to {longest:0.#} seconds.", "sourcePath");
            return new AiVideoEditNodeRequest(extend ? "video.extend" : "video.edit")
            {
                Mode = extend ? AiVideoEditMode.Extend : AiVideoEditMode.Edit,
                Prompt = text,
                DurationSeconds = duration,
                SourceVideo = source,
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine([modeId, text, extend ? duration.ToString(CultureInfo.InvariantCulture) : null, model]),
            };
        },
        precheck: () =>
        {
            if (modeId is not ("edit" or "extend"))
                throw Invalid($"Unknown mode '{mode}'. Use edit or extend.", "mode");
        });
    }

    [McpServerTool(Name = "transcribe_audio")]
    [Description("Transcribes the speech in an audio or video file and returns timed segments, and words when the model reports them. Long files are sent in ten-minute parts. Paid by length.")]
    public async ValueTask<ToolResult<AgentAiJobSnapshot>> TranscribeAudio(
        [Description("Path of the audio or video file, in the workspace or among the open scene's AI results.")]
        string sourcePath,
        [Description("Language code such as ja or en; omit to detect it.")]
        string? language = null,
        [Description("Model id from list_ai_models(audio.transcribe); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<AgentAiJobSnapshot>(async () =>
        {
            RequireWait(waitSeconds);
            string? languageCode = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
            if (languageCode is not null && !AiRequestLimits.IsIso6391LanguageCode(languageCode))
                throw Invalid($"'{language}' is not a two-letter ISO 639-1 language code such as ja or en.", "language");
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            // Transcription has no executor to resolve the default model, so the one chosen here is sent.
            GenerativeModelInfo? chosen = await ResolveModelAsync("audio.transcribe", model, cancellationToken).ConfigureAwait(false);
            string? modelId = chosen?.Id ?? NormalizeModel(model);
            Scene? scene = await FindSceneAsync().ConfigureAwait(false);
            string path = RequireFile(scene, sourcePath, "sourcePath");
            string jobId = jobs.Start("audio.transcribe", async (progress, token) =>
            {
                AgentTranscript transcript = await backend
                    .TranscribeAsync(path, languageCode, modelId, progress, token)
                    .ConfigureAwait(false);
                return new AgentAiJobOutput(null, "transcript", modelId, null, transcript);
            });
            return await WaitAsync(jobId, waitSeconds, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

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
    [Description("Stops waiting for an AI job. A request the service has already accepted is still charged; its result stays collectable from the AI tab's job history in the app.")]
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
        CancellationToken cancellationToken,
        Func<Scene, GenerativeModelInfo?, GenerativeRequest> build,
        Action? precheck = null)
    {
        return ExecuteAsync<AgentAiJobSnapshot>(async () =>
        {
            RequireWait(waitSeconds);
            precheck?.Invoke();
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            Scene scene = await RequireSceneAsync().ConfigureAwait(false);
            // Omitted settings are chosen from what this model offers, as the AI tab's controls are.
            GenerativeModelInfo? chosen = await ResolveModelAsync(operation, model, cancellationToken).ConfigureAwait(false);
            // Inputs are read now, so a missing file or a bad argument is the call's error, not the job's.
            GenerativeRequest request;
            try
            {
                request = build(scene, chosen);
            }
            catch (GenerativeExecutionException ex)
            {
                throw Invalid(ex.Message, null);
            }

            await RequireUploadSizesAsync(request, cancellationToken).ConfigureAwait(false);
            IGenerativeNodeExecutor executor = backend.CreateExecutor(scene);
            string jobId = jobs.Start(operation, async (progress, token) =>
            {
                GenerativeExecutionResult result;
                try
                {
                    result = await executor
                        .ExecuteAsync(request, new StatusProgress(progress), token)
                        .ConfigureAwait(false);
                }
                catch (GenerativeExecutionException ex)
                {
                    throw new AgentAiException(ErrorCode.AiGenerationFailed, ex.Message);
                }

                return new AgentAiJobOutput(
                    result.ResultFile.LocalPath,
                    result.IsVideo ? "video" : "image",
                    result.ModelId,
                    result.Seed);
            });
            return await WaitAsync(jobId, waitSeconds, cancellationToken).ConfigureAwait(false);
        });
    }

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

    // As the executor resolves it: the named model, or the default one the account can use. An empty
    // catalog (offline) leaves the choice to the service, with the AI tab's fallback lists.
    private async Task<GenerativeModelInfo?> ResolveModelAsync(string operation, string? model, CancellationToken cancellationToken)
    {
        IReadOnlyList<GenerativeModelInfo> offered =
            await backend.GetModelsAsync(operation, cancellationToken).ConfigureAwait(false);
        if (NormalizeModel(model) is { } id)
        {
            GenerativeModelInfo? named = offered.FirstOrDefault(candidate => candidate.Id == id);
            if (offered.Count > 0 && named is not { IsAvailable: true })
                throw Invalid($"The model '{id}' is not available for {operation}. Call list_ai_models(\"{operation}\") for the ones this account can use.", "model");
            return named;
        }

        return offered.FirstOrDefault(candidate => candidate.IsAvailable && candidate.IsDefault)
               ?? offered.FirstOrDefault(candidate => candidate.IsAvailable);
    }

    // The upload sizes the executor refuses inside the job, measured on the PNGs it would send.
    private async Task RequireUploadSizesAsync(GenerativeRequest request, CancellationToken cancellationToken)
    {
        const long MB = 1024 * 1024;
        switch (request)
        {
            case AiImageGenerationNodeRequest { References.Count: > 0 } image:
                if (image.References.Any(reference => reference.EncodedPng.LongLength > AiRequestLimits.MaxImageUploadBytes))
                    throw TooLarge("A reference picture", AiRequestLimits.MaxImageUploadBytes, "referenceImagePaths");
                long budget = await backend.GetImageReferenceBudgetAsync(cancellationToken).ConfigureAwait(false);
                if (image.References.Sum(reference => reference.EncodedPng.LongLength) > budget)
                    throw Invalid($"The reference pictures come to more than the {budget / (double)MB:0.#} MB one generation takes in all.", "referenceImagePaths");
                break;
            // An outpaint uploads the expanded canvas; built here with the executor's own code to measure it.
            case AiImageEditNodeRequest edit
                when (edit.Task == AiImageEditTask.Outpaint
                        ? AiGenerativeNodeExecutor.ExpandCanvas(edit.Image.EncodedPng, edit.OutpaintExpansionPercent ?? 25).LongLength
                        : edit.Image.EncodedPng.LongLength) > AiRequestLimits.MaxImageUploadBytes:
                throw TooLarge("The picture", AiRequestLimits.MaxImageUploadBytes, "sourcePath");
            case AiVideoGenerationNodeRequest video
                when video.FirstFrame?.EncodedPng.LongLength > AiRequestLimits.MaxFrameUploadBytes
                     || video.LastFrame?.EncodedPng.LongLength > AiRequestLimits.MaxFrameUploadBytes:
                throw TooLarge("A frame picture", AiRequestLimits.MaxFrameUploadBytes, video.FirstFrame?.EncodedPng.LongLength > AiRequestLimits.MaxFrameUploadBytes ? "firstFramePath" : "lastFramePath");
        }

        static ReconcileException TooLarge(string what, long limit, string target)
            => new(new ToolError(ErrorCode.MediaUnsupported, $"{what} is larger than {limit / MB} MB once saved as PNG.", target));
    }

    // The executor builds the expanded canvas inside the job and refuses one over the AI image limits.
    private static void RequireOutpaintCanvas(PixelSize source, int expansionPercent)
    {
        (int width, int height, _, _) = AiImageEditTasks.GetOutpaintDimensions(source.Width, source.Height, expansionPercent);
        try
        {
            AiImageDecodeValidator.ValidateDimensions(width, height);
        }
        catch (InvalidDataException)
        {
            throw Invalid(
                $"Outpainting by {expansionPercent}% makes a {width}x{height} canvas, over the {AiImageDecodeValidator.MaxDimension} pixels a side the AI service takes. Use a smaller percentage or picture.",
                "outpaintExpansionPercent");
        }
    }

    // As the executor checks a request against the model, but as the call's error, before a job starts.
    private static void RequireOffered<T>(T value, IReadOnlyList<T> offered, string modelId, string target)
    {
        if (!offered.Contains(value))
            throw Invalid($"The model '{modelId}' does not take {target} {value}. It takes: {string.Join(", ", offered)}.", target);
    }

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
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.AiUnavailable,
                reason,
                null,
                "AI tools run on the account signed in to the Beutl app; sign in there, then call the tool again."));
        }
    }

    private Task<Scene?> FindSceneAsync()
        => Dispatcher.UIThread.InvokeAsync(
            () => (editorService.SelectedTabItem.Value?.Context.Value as EditViewModel)?.Scene).GetTask();

    // Results are kept with the scene, so they are saved and moved with the project.
    private async Task<Scene> RequireSceneAsync()
    {
        Scene? scene = await FindSceneAsync().ConfigureAwait(false);
        return scene ?? throw new ReconcileException(new ToolError(
            ErrorCode.NoActiveEditorSession,
            "AI results are saved next to the open scene, and no scene is open in the editor.",
            null,
            "Open or create a project in the Beutl editor (or call create_project/open_project), then call the tool again."));
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
            image?.SupportsSeed ?? video?.SupportsSeed);
    }

    private static string? NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    private static string RequirePrompt(string? prompt, int maxLength = AiRequestLimits.MaxPromptLength)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw Invalid("A prompt is required.", "prompt");
        string text = prompt.Trim();
        return text.Length > maxLength
            ? throw Invalid($"The prompt is {text.Length} characters; this request takes at most {maxLength}.", "prompt")
            : text;
    }

    private static int MaxPromptLength(GenerativeVideoCapabilities? model)
        => Math.Min(model?.MaxPromptLength ?? int.MaxValue, AiRequestLimits.MaxPromptLength);

    // The executor drops a seed the model does not take; refused instead, since the caller asked
    // for a result it can reproduce.
    private static void RequireSeed(int? seed, bool? supportsSeed, string? modelId)
    {
        if (seed is not { } value)
            return;
        if (value < AiRequestLimits.MinSeed)
            throw Invalid($"seed must be between {AiRequestLimits.MinSeed} and {AiRequestLimits.MaxSeed}.", "seed");
        if (supportsSeed == false)
            throw Invalid($"The model '{modelId}' does not take a seed. Omit seed, or choose a model whose supportsSeed is true.", "seed");
    }

    // An input is uploaded to the AI service, so a path an agent passes must not reach files the user
    // never shared with it: only the workspace and the open scene's AI results (to build on an earlier
    // result when the project lives outside the workspace) are readable. Relative paths are
    // workspace-relative, and the file read is the one checked, after following links.
    private string RequireFile(Scene? scene, string? path, string target)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Invalid("A file path is required.", target);
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path));
        // Checked before existence, so a refusal says nothing about what lies outside.
        string resolved = PathBoundary.ResolveExistingPath(full);
        if (!FilePathComparison.IsSameOrDescendant(workspace.Root, resolved)
            && (scene is null || !FilePathComparison.IsSameOrDescendant(AiResultImporter.GetResourceDirectory(scene), resolved)))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.WorkspaceBoundary,
                $"'{path}' is outside the workspace, and AI inputs are uploaded to the service.",
                target,
                $"Use a file inside the workspace ({workspace.Root}) or one an earlier AI tool saved for the open scene."));
        }

        return File.Exists(resolved)
            ? resolved
            : throw new ReconcileException(new ToolError(ErrorCode.MediaNotFound, $"No file exists at '{path}'.", target));
    }

    private GenerativeImageInput ReadImage(Scene scene, string path, string name, long maxBytes)
        => ReadImage(scene, path, name, maxBytes, out _);

    private GenerativeImageInput ReadImage(Scene scene, string path, string name, long maxBytes, out PixelSize size)
    {
        string full = RequireFile(scene, path, name);
        try
        {
            // Size and dimensions are checked before decoding, as the AI dialogs check them, so a
            // small file that would decode to a huge picture is refused unread.
            using Bitmap bitmap = AiImageDecodeValidator.LoadValidatedBitmap(full, maxBytes);
            size = new PixelSize(bitmap.Width, bitmap.Height);
            using var stream = new MemoryStream();
            if (!bitmap.Save(stream, EncodedImageFormat.Png))
                throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", name));
            return new GenerativeImageInput($"{name}.png", stream.ToArray());
        }
        catch (InvalidDataException)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.MediaUnsupported,
                $"'{path}' is not a picture the AI service accepts: it must be a readable image of at most {maxBytes / (1024 * 1024)} MB and {AiImageDecodeValidator.MaxDimension} pixels a side.",
                name));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", name));
        }
    }

    private (GenerativeFileInput Input, double Seconds) ReadVideo(Scene scene, string path)
    {
        string full = RequireFile(scene, path, "sourcePath");
        if (!GenerativeInputs.IsSupportedVideoFile(full))
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, "Only mp4 and webm clips can be edited.", "sourcePath"));
        GenerativeFileInput input = GenerativeInputs.ReadVideoFile(full, "source");
        // Opened now, as the executor opens it to read its length, so a file that is not a clip is
        // the call's media_unsupported rather than a failed job.
        try
        {
            using MediaReader reader = MediaReader.Open(full, new MediaOptions(MediaMode.Video) { PreferProxy = false });
            if (reader.HasVideo)
                return (input, reader.VideoInfo.Duration.ToDouble());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be opened as a video.", "sourcePath"));
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

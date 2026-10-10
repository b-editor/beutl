using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Graphics;
using Beutl.Media;
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
    private const string WaitDescription =
        "Seconds to wait for the result before returning (0-110, default 45). A job still running returns status Running with its jobId; call read_ai_job to wait again.";

    private static readonly string[] s_imageTasks = ["remove_background", "upscale", "restyle", "remove_object", "outpaint"];

    [McpServerTool(Name = "list_ai_models")]
    [Description("Lists the AI models the signed-in account can use for one operation, with the aspect ratios, durations, resolutions and inputs each accepts. Operations: image.generate, image.edit.remove_background, image.edit.upscale, image.edit.restyle, image.edit.remove_object, image.edit.outpaint, video.generate, video.edit, video.extend, audio.transcribe.")]
    public ValueTask<ToolResult<ListAiModelsResponse>> ListAiModels(
        [Description("The operation id, such as video.generate.")]
        string operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<ListAiModelsResponse>(async () =>
        {
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<GenerativeModelInfo> models =
                await backend.GetModelsAsync(operation.Trim(), cancellationToken).ConfigureAwait(false);
            return new ListAiModelsResponse(operation.Trim(), models.Select(Summarize).ToArray());
        });
    }

    [McpServerTool(Name = "generate_image")]
    [Description("Generates a picture from a prompt with the signed-in account's AI credits and saves it as a PNG next to the open scene. Returns the file path when done; place it with apply_edit. Paid per call.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> GenerateImage(
        [Description("What to draw.")]
        string prompt,
        [Description("Aspect ratio such as 16:9 or 1:1. Defaults to the one nearest the scene.")]
        string? aspectRatio = null,
        [Description("Background: auto, opaque or transparent.")]
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
        return StartAsync("image.generate", waitSeconds, cancellationToken, scene =>
        {
            string text = RequirePrompt(prompt);
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(GenerativeImageCapabilities.DefaultAspectRatios, scene.FrameSize, "16:9")
                : aspectRatio.Trim();
            GenerativeImageInput[] references = (referenceImagePaths ?? [])
                .Select((path, index) => ReadImage(scene, path, $"reference-{index + 1}"))
                .ToArray();
            string chosenBackground = string.IsNullOrWhiteSpace(background) ? "auto" : background.Trim();
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
        return StartAsync($"image.edit.{taskId}", waitSeconds, cancellationToken, scene =>
        {
            int index = Array.IndexOf(s_imageTasks, taskId);
            if (index < 0)
                throw Invalid($"Unknown edit task '{task}'. Use one of: {string.Join(", ", s_imageTasks)}.", "task");
            var editTask = (AiImageEditTask)index;
            string? text = editTask.RequiresPrompt() ? RequirePrompt(prompt) : null;
            return new AiImageEditNodeRequest($"image.edit.{taskId}")
            {
                Task = editTask,
                Prompt = text,
                OutpaintExpansionPercent = editTask == AiImageEditTask.Outpaint ? outpaintExpansionPercent : null,
                Image = ReadImage(scene, sourcePath, "source"),
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine([taskId, text, model, outpaintExpansionPercent.ToString(CultureInfo.InvariantCulture)]),
            };
        });
    }

    [McpServerTool(Name = "generate_video")]
    [Description("Generates a video clip from a prompt, optionally starting on and ending at given pictures, and saves it next to the open scene. Takes minutes: returns a running job unless it finishes within waitSeconds. Paid per call; longer and higher-resolution clips cost more.")]
    public ValueTask<ToolResult<AgentAiJobSnapshot>> GenerateVideo(
        [Description("What happens in the clip.")]
        string prompt,
        [Description("Length in seconds; must be one the model offers (see list_ai_models(video.generate)). Default 6.")]
        int durationSeconds = GenerativeVideoCapabilities.DefaultDuration,
        [Description("Resolution such as 720p or 1080p. Defaults to the smallest that covers the scene.")]
        string? resolution = null,
        [Description("Aspect ratio such as 16:9 or 9:16. Defaults to the one nearest the scene.")]
        string? aspectRatio = null,
        [Description("Generate sound with the picture, when the model supports it.")]
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
        return StartAsync("video.generate", waitSeconds, cancellationToken, scene =>
        {
            string text = RequirePrompt(prompt);
            if (lastFramePath is not null && firstFramePath is null)
                throw Invalid("A last frame needs a first frame.", "lastFramePath");
            string chosenResolution = string.IsNullOrWhiteSpace(resolution)
                ? GenerativeShapeSuggestion.SuggestResolution(GenerativeVideoCapabilities.DefaultResolutions, scene.FrameSize)
                : resolution.Trim();
            string ratio = string.IsNullOrWhiteSpace(aspectRatio)
                ? GenerativeShapeSuggestion.NearestAspectRatio(GenerativeVideoCapabilities.DefaultAspectRatios, scene.FrameSize, "16:9")
                : aspectRatio.Trim();
            return new AiVideoGenerationNodeRequest("video.generate")
            {
                Prompt = text,
                DurationSeconds = durationSeconds,
                Resolution = chosenResolution,
                AspectRatio = ratio,
                GenerateAudio = generateAudio,
                Seed = seed,
                FirstFrame = firstFramePath is null ? null : ReadImage(scene, firstFramePath, "first-frame"),
                LastFrame = lastFramePath is null ? null : ReadImage(scene, lastFramePath, "last-frame"),
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine(
                    [text, durationSeconds.ToString(CultureInfo.InvariantCulture), chosenResolution, ratio, generateAudio ? "audio" : null, model, seed?.ToString(CultureInfo.InvariantCulture)]),
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
        [Description("For extend: seconds to add; must be one the model offers (see list_ai_models(video.extend)). Default 6.")]
        int durationSeconds = GenerativeVideoCapabilities.DefaultDuration,
        [Description("Model id from list_ai_models(video.edit or video.extend); omit for the default.")]
        string? model = null,
        [Description(WaitDescription)]
        int waitSeconds = 45,
        CancellationToken cancellationToken = default)
    {
        string modeId = mode.Trim().ToLowerInvariant();
        return StartAsync(modeId == "extend" ? "video.extend" : "video.edit", waitSeconds, cancellationToken, scene =>
        {
            if (modeId is not ("edit" or "extend"))
                throw Invalid($"Unknown mode '{mode}'. Use edit or extend.", "mode");
            bool extend = modeId == "extend";
            string text = RequirePrompt(prompt);
            return new AiVideoEditNodeRequest(extend ? "video.extend" : "video.edit")
            {
                Mode = extend ? AiVideoEditMode.Extend : AiVideoEditMode.Edit,
                Prompt = text,
                DurationSeconds = extend ? durationSeconds : 0,
                SourceVideo = ReadVideo(scene, sourcePath),
                ModelId = NormalizeModel(model),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = GenerativeFingerprint.Combine([modeId, text, extend ? durationSeconds.ToString(CultureInfo.InvariantCulture) : null, model]),
            };
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
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            Scene? scene = await FindSceneAsync().ConfigureAwait(false);
            string path = RequireFile(scene, sourcePath, "sourcePath");
            string jobId = jobs.Start("audio.transcribe", async (progress, token) =>
            {
                AgentTranscript transcript = await backend
                    .TranscribeAsync(path, language, model, progress, token)
                    .ConfigureAwait(false);
                return new AgentAiJobOutput(null, "transcript", NormalizeModel(model), null, transcript);
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
        return ExecuteAsync(() => new ValueTask<AgentAiJobSnapshot>(WaitAsync(jobId, waitSeconds, cancellationToken)));
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
        int waitSeconds,
        CancellationToken cancellationToken,
        Func<Scene, GenerativeRequest> build)
    {
        return ExecuteAsync<AgentAiJobSnapshot>(async () =>
        {
            await RequireAvailableAsync(cancellationToken).ConfigureAwait(false);
            Scene scene = await RequireSceneAsync().ConfigureAwait(false);
            // Inputs are read now, so a missing file or a bad argument is the call's error, not the job's.
            GenerativeRequest request;
            try
            {
                request = build(scene);
            }
            catch (GenerativeExecutionException ex)
            {
                throw Invalid(ex.Message, null);
            }

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
        TimeSpan wait = TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0, 110));
        return await jobs.WaitAsync(jobId, wait, cancellationToken).ConfigureAwait(false)
               ?? throw NotFound(jobId);
    }

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

    private static AiModelSummary Summarize(GenerativeModelInfo model)
        => new(
            model.Id,
            model.Label,
            model.IsDefault,
            model.IsAvailable,
            model.Image?.AspectRatios ?? model.Video?.AspectRatios,
            model.Image?.Backgrounds,
            model.Image?.MaxReferenceImages,
            model.Video?.DurationsSeconds,
            model.Video?.Resolutions,
            model.Video?.SupportsAudio,
            model.Video?.SupportsFirstFrame,
            model.Video?.SupportsLastFrame,
            model.Image?.SupportsSeed ?? model.Video?.SupportsSeed);

    private static string? NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    private static string RequirePrompt(string? prompt)
        => string.IsNullOrWhiteSpace(prompt)
            ? throw Invalid("A prompt is required.", "prompt")
            : prompt.Trim();

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

    private GenerativeImageInput ReadImage(Scene scene, string path, string name)
    {
        string full = RequireFile(scene, path, name);
        try
        {
            using Bitmap bitmap = Bitmap.FromFile(full);
            using var stream = new MemoryStream();
            if (!bitmap.Save(stream, EncodedImageFormat.Png))
                throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", name));
            return new GenerativeImageInput($"{name}.png", stream.ToArray());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, $"'{path}' could not be read as a picture.", name));
        }
    }

    private GenerativeFileInput ReadVideo(Scene scene, string path)
    {
        string full = RequireFile(scene, path, "sourcePath");
        if (!GenerativeInputs.IsSupportedVideoFile(full))
            throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, "Only mp4 and webm clips can be edited.", "sourcePath"));
        return GenerativeInputs.ReadVideoFile(full, "source");
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

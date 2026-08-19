using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal interface IAiCaptionResultPresenter
{
    bool TryPresentCaptionResult(AiCaptionHistoryResult result);
}

internal sealed class AiJobResultContext(
    IAiJobResultEditorContext editor,
    IAuthenticatedContentService content,
    Action<AiCaptionHistoryResult>? openCaptionResult) : IAiJobResultContext, IAiCaptionResultPresenter
{
    private readonly IAiJobResultEditorContext _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly IAuthenticatedContentService _content = content ?? throw new ArgumentNullException(nameof(content));

    public IAiJobResultEditorContext Editor => _editor;

    public Task<AiContentDownload> CopyContentToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken)
        => _content.CopyToAsync(contentUri, destination, cancellationToken);

    public bool TryPresentCaptionResult(AiCaptionHistoryResult result)
    {
        if (openCaptionResult is null)
            return false;

        openCaptionResult(result);
        return true;
    }
}

internal static class BuiltInAiJobResultHandlers
{
    public static IReadOnlyList<AiJobResultHandlerRegistration> Create()
    {
        return
        [
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                AiJobKinds.Image,
                new ImageAiJobResultHandler(
                    () => Strings.AiImageGeneration,
                    _ => AiOperations.ImageGeneration.Value))),
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                AiJobKinds.ImageEdit,
                new ImageAiJobResultHandler(
                    () => Strings.AiImageEdit,
                    GetImageEditOperation))),
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                AiJobKinds.Transcription,
                new CaptionAiJobResultHandler(
                    () => Strings.AiSubtitle))),
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                AiJobKinds.CaptionTranslation,
                new CaptionAiJobResultHandler(
                    () => Strings.AiSubtitle,
                    useTargetLanguageAsFallback: true))),
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                AiJobKinds.Video,
                new VideoAiJobResultHandler())),
        ];
    }

    private static string GetImageEditOperation(AiJob job)
    {
        string? task = AiJobResultInput.GetString(job, "task");
        return task is null
            ? "image.edit"
            : $"image.edit.{task.Replace('_', '.')}";
    }
}

internal abstract class BuiltInAiJobResultHandler(
    Func<string> getKindDisplayName,
    bool useTargetLanguageAsFallback = false) : IAiJobResultHandler
{
    protected virtual bool HasImagePreview => false;

    private static readonly IReadOnlyDictionary<string, Func<string>> s_statusDisplayNames =
        new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [AiJobStatuses.Queued.Value] = () => Strings.AiJobCenter_StatusQueued,
            [AiJobStatuses.Running.Value] = () => Strings.AiJobCenter_StatusRunning,
            [AiJobStatuses.Finalizing.Value] = () => Strings.AiJobCenter_StatusRunning,
            [AiJobStatuses.Succeeded.Value] = () => Strings.AiJobCenter_StatusSucceeded,
            [AiJobStatuses.Failed.Value] = () => Strings.AiJobCenter_StatusFailed,
            [AiJobStatuses.Canceled.Value] = () => Strings.AiJobCenter_StatusCanceled,
        };

    public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
    {
        string? prompt = AiJobResultInput.GetString(job, "prompt");
        string? language = AiJobResultInput.GetString(job, "targetLanguage")
            ?? AiJobResultInput.GetString(job, "language");
        string summary = prompt
            ?? AiJobResultInput.GetString(job, "filename")
            ?? (useTargetLanguageAsFallback && language is not null
                ? string.Format(Strings.AiJobCenter_TranslationSummary, language)
                : Strings.AiJobCenter_NoDescription);
        string normalizedStatus = job.Status.Value.Trim();
        string statusDisplayName = s_statusDisplayNames.TryGetValue(
            normalizedStatus,
            out Func<string>? getStatusDisplayName)
            ? getStatusDisplayName()
            : normalizedStatus;

        var details = new List<string>();
        AddIfPresent(details, AiJobResultInput.GetString(job, "size"));
        if (AiJobResultInput.GetInt32(job, "durationSeconds") is { } durationSeconds)
        {
            details.Add($"{durationSeconds} {Strings.AiVideoSeconds}");
        }
        AddIfPresent(details, AiJobResultInput.GetString(job, "resolution"));
        AddIfPresent(details, language);

        return new AiJobPresentation(
            getKindDisplayName(),
            statusDisplayName,
            summary,
            string.Join(" · ", details),
            status.Outcome == AiJobOutcomes.Failed,
            HasImagePreview);
    }

    public AiJobCompletionPresentation? CreateCompletion(
        AiJob job,
        AiJobStatusSemantics status,
        AiJobPresentation presentation)
    {
        if (!status.IsTerminal)
            return null;

        if (status.Outcome == AiJobOutcomes.Succeeded)
        {
            return new AiJobCompletionPresentation(
                Strings.AiJobCenter,
                string.Format(
                    Strings.AiJobCenter_CompletedNotification,
                    presentation.KindDisplayName),
                AiJobNotificationKind.Success,
                TimeSpan.FromSeconds(15));
        }

        if (status.Outcome == AiJobOutcomes.Failed)
        {
            return new AiJobCompletionPresentation(
                Strings.AiJobCenter,
                string.Format(
                    Strings.AiJobCenter_FailedNotification,
                    presentation.KindDisplayName),
                AiJobNotificationKind.Warning,
                TimeSpan.FromSeconds(20));
        }

        if (status.Outcome == AiJobOutcomes.Canceled)
        {
            return new AiJobCompletionPresentation(
                Strings.AiJobCenter,
                string.Format(
                    Strings.AiJobCenter_CanceledNotification,
                    presentation.KindDisplayName),
                AiJobNotificationKind.Information,
                TimeSpan.FromSeconds(15));
        }

        return null;
    }

    public virtual bool CanHandle(AiJob job, AiJobStatusSemantics status)
        => status.Outcome == AiJobOutcomes.Succeeded && job.ContentUri is not null;

    public abstract Task HandleAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken);

    protected static void ShowImportResult(ElementAddResult result)
    {
        if (result.Failure is LockedElementLayerFailure)
        {
            NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
        }
        else if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to add the AI job result: {result.Failure?.Id}.",
                result.Failure?.Exception);
        }
        else
        {
            NotificationService.ShowSuccess(Strings.AiJobCenter, Strings.AiJobCenter_AddedToScene);
        }
    }

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (value is not null)
        {
            values.Add(value);
        }
    }
}

internal sealed class ImageAiJobResultHandler : BuiltInAiJobResultHandler
{
    private readonly Func<string> _getDisplayName;
    private readonly Func<AiJob, string> _getOperation;

    protected override bool HasImagePreview => true;

    public ImageAiJobResultHandler(
        Func<string> getDisplayName,
        Func<AiJob, string> getOperation)
        : base(getDisplayName)
    {
        _getDisplayName = getDisplayName ?? throw new ArgumentNullException(nameof(getDisplayName));
        _getOperation = getOperation ?? throw new ArgumentNullException(nameof(getOperation));
    }

    public override async Task HandleAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        await context.CopyContentToAsync(job.ContentUri!, content, cancellationToken);
        content.Position = 0;
        using Bitmap bitmap = Bitmap.FromStream(content);

        IAiJobResultEditorContext editor = context.Editor;
        TimeSpan start = editor.CurrentTime;
        var importer = new AiResultImporter(editor.Scene, editor.ElementAdder);
        ElementAddResult result = await importer.ImportImageAsync(
            bitmap,
            new AiResultImportOptions(
                start,
                TimeSpan.FromSeconds(5),
                editor.GetNextLayer(start),
                _getDisplayName()),
            cancellationToken);
        ShowImportResult(result);
    }
}

internal sealed class VideoAiJobResultHandler()
    : BuiltInAiJobResultHandler(() => Strings.AiVideoGeneration)
{
    public override async Task HandleAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken)
    {
        (string temporaryContentPath, FileStream destination) = AiTemporaryFileStore.Create(
            "downloads",
            "job-video",
            ".download");
        try
        {
            AiContentDownload download;
            await using (destination)
            {
                download = await context.CopyContentToAsync(
                    job.ContentUri!,
                    destination,
                    cancellationToken);
            }
            AiContentMetadata? metadata = AiContentMetadata.Combine(
                job.ContentMetadata,
                download.Metadata);
            string extension = metadata?.GetFileExtension(".mp4", "video") ?? ".mp4";

            int? durationSeconds = AiJobResultInput.GetInt32(job, "durationSeconds");
            IAiJobResultEditorContext editor = context.Editor;
            TimeSpan start = editor.CurrentTime;
            var importer = new AiResultImporter(editor.Scene, editor.ElementAdder);
            ElementAddResult result = await importer.ImportVideoAsync(
                temporaryContentPath,
                extension,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(durationSeconds ?? 6),
                    editor.GetNextLayer(start),
                    Strings.AiVideoGeneration),
                cancellationToken);
            ShowImportResult(result);
        }
        finally
        {
            TryDelete(temporaryContentPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}

internal sealed class CaptionAiJobResultHandler(
    Func<string> getKindDisplayName,
    bool useTargetLanguageAsFallback = false)
    : BuiltInAiJobResultHandler(getKindDisplayName, useTargetLanguageAsFallback)
{
    public override async Task HandleAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken)
    {
        AiCaptionHistoryResult recovered = await DownloadAsync(job, context, cancellationToken);
        if (context is IAiCaptionResultPresenter presenter
            && presenter.TryPresentCaptionResult(recovered))
        {
            return;
        }

        var document = new CaptionDocument(recovered.Segments.Select(segment => new CaptionCue(
            TimeSpan.FromSeconds(segment.Start),
            TimeSpan.FromSeconds(segment.End),
            segment.Text,
            language: recovered.Language)));
        var templates = new CaptionTemplateRegistry(
        [
            CaptionTemplateDefaults.CreateDefaultText(Strings.AiSubtitle_DefaultTemplate),
        ]);
        CaptionSceneImportResult result = await AiCaptionSceneImporter.AddAsync(
            context.Editor.Scene,
            context.Editor.ElementAdder,
            document,
            templates,
            CaptionTemplateIds.DefaultText,
            cancellationToken);
        if (result.IsSuccess)
        {
            NotificationService.ShowSuccess(Strings.AiJobCenter, Strings.AiJobCenter_AddedToScene);
        }
        else if (result.FailureId == ElementAddFailureIds.LockedLayer)
        {
            NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
        }
        else
        {
            throw new InvalidOperationException(
                $"Failed to add the AI caption result: {result.FailureId}.");
        }
    }

    private static async Task<AiCaptionHistoryResult> DownloadAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken)
    {
        using var content = new SizeLimitedMemoryStream(
            AiCaptionHistoryResultParser.MaximumResultBytes);
        await context.CopyContentToAsync(job.ContentUri!, content, cancellationToken);
        if (!AiCaptionHistoryResultParser.TryParse(
                content.GetBuffer().AsSpan(0, checked((int)content.Length)),
                job.Kind.Value,
                job.Id,
                out AiCaptionHistoryResult? result)
            || result is null)
        {
            throw new InvalidDataException("The AI caption history result is invalid.");
        }

        return result;
    }
}

internal static class AiJobResultInput
{
    public static string? GetString(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static int? GetInt32(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            return null;
        }

        return result;
    }
}

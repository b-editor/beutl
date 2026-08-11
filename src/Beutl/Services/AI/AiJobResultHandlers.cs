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
using Beutl.ViewModels;

namespace Beutl.Services.AI;

internal sealed class AiJobResultContext(
    EditViewModel editor,
    IAuthenticatedContentService content,
    Action<AiCaptionHistoryResult>? openCaptionResult) : IAiJobResultContext
{
    private readonly EditViewModel _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly IAuthenticatedContentService _content = content ?? throw new ArgumentNullException(nameof(content));

    public IEditorContext Editor => _editor;

    internal Action<AiCaptionHistoryResult>? OpenCaptionResult { get; } = openCaptionResult;

    public Task CopyContentToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken)
        => _content.CopyToAsync(contentUri, destination, cancellationToken);

    public int GetNextLayer(TimeSpan start)
    {
        return _editor.Scene.Children
            .Where(item => item.Start <= start && start < item.Range.End)
            .Select(item => item.ZIndex)
            .DefaultIfEmpty(-1)
            .Max() + 1;
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
            status.Outcome == AiJobOutcomes.Failed);
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

    protected static EditViewModel GetEditor(IAiJobResultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Editor as EditViewModel
            ?? throw new InvalidOperationException("The AI job result context requires a Beutl edit view model.");
    }

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

        EditViewModel editor = GetEditor(context);
        TimeSpan start = editor.Player.CurrentFrame.Value;
        var importer = new AiResultImporter(editor);
        ElementAddResult result = await importer.ImportImageAsync(
            bitmap,
            new AiResultImportOptions(
                start,
                TimeSpan.FromSeconds(5),
                context.GetNextLayer(start),
                _getDisplayName(),
                AiProvenanceFactory.ImportedHistoryResult(
                    _getOperation(job),
                    AiJobResultInput.GetString(job, "size"),
                    null,
                    null,
                    AiJobResultInput.GetString(job, "task"),
                    job.CreatedAt.ToUniversalTime())),
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
        string directory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Downloads");
        Directory.CreateDirectory(directory);
        string temporaryContentPath = Path.Combine(directory, $"{Guid.NewGuid():N}.mp4");
        try
        {
            await using (FileStream destination = new(
                temporaryContentPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                await context.CopyContentToAsync(job.ContentUri!, destination, cancellationToken);
            }

            int? durationSeconds = AiJobResultInput.GetInt32(job, "durationSeconds");
            EditViewModel editor = GetEditor(context);
            TimeSpan start = editor.Player.CurrentFrame.Value;
            var importer = new AiResultImporter(editor);
            ElementAddResult result = await importer.ImportVideoAsync(
                temporaryContentPath,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(durationSeconds ?? 6),
                    context.GetNextLayer(start),
                    Strings.AiVideoGeneration,
                    AiProvenanceFactory.ImportedHistoryResult(
                        AiOperations.VideoGeneration.Value,
                        null,
                        durationSeconds,
                        AiJobResultInput.GetString(job, "resolution"),
                        null,
                        job.CreatedAt.ToUniversalTime())),
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
        if (context is AiJobResultContext { OpenCaptionResult: { } openCaptionResult })
        {
            openCaptionResult(recovered);
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
            GetEditor(context),
            document,
            templates,
            CaptionTemplateIds.DefaultText,
            recovered.Provenance,
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
                job.CreatedAt.ToUniversalTime(),
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

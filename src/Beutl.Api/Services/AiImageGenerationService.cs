using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api.Services;

internal sealed class AiImageGenerationService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiImageGenerationService
{
    private const int MaximumImagePreviewCount = 4;

    public Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        CancellationToken cancellationToken)
        => GenerateAsync(request, null, cancellationToken);

    public async Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        IProgress<AiImagePreview>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        // The endpoint reads "auto" and an absent background the same way, so
        // leaving it to the model is sent as nothing at all.
        string backgroundValue = request.Background.Value;
        string? background =
            backgroundValue.Length > 0 && backgroundValue != "auto" ? backgroundValue : null;

        if (request.References.Count == 0)
            return await GeneratePromptOnlyAsync(request, progress, idempotencyKey, background, cancellationToken);

        return await GenerateFromReferencesAsync(request, idempotencyKey, background, cancellationToken);
    }

    private async Task<AiImageResult> GeneratePromptOnlyAsync(
        AiImageGenerationRequest request,
        IProgress<AiImagePreview>? progress,
        string idempotencyKey,
        string? background,
        CancellationToken cancellationToken)
    {
        var body = new CreateAiImageRequest
        {
            Prompt = request.Prompt,
            AspectRatio = request.AspectRatio.Value,
            Background = background,
            Seed = request.Seed,
            Model = request.Model?.Value,
        };
        if (progress is null)
        {
            return await ExecuteAsync(
                "AiImageGenerationService.Generate",
                (authorization, token) => Application.Ai.CreateImage(
                    authorization,
                    idempotencyKey,
                    body,
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetImageTags(activity, request));
        }

        var previewBudget = new ImagePreviewBudget();
        return await ExecuteStreamingAsync(
            "AiImageGenerationService.Generate",
            () => JsonRequest("/api/v3/ai/images", idempotencyKey, body),
            item => ReportImagePreview(item, progress, previewBudget),
            data => AiModelMapper.ToModel(
                JsonSerializer.Deserialize<AiImageResponse>(
                    data,
                    AiStreamJson.Options)
                ?? throw new AiException("The AI image result was empty.")),
            cancellationToken,
            activity => SetImageTags(activity, request));
    }

    private async Task<AiImageResult> GenerateFromReferencesAsync(
        AiImageGenerationRequest request,
        string idempotencyKey,
        string? background,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Every reference is held open for the whole upload, so they are opened
        // together and closed together — one that failed to open must not leave
        // the ones before it behind.
        var streams = new List<Stream>(request.References.Count);
        try
        {
            List<StreamPart> referenceParts = await OpenReferencePartsAsync(
                request.References,
                AiRequestLimits.MaxImageUploadBytes,
                request.ReferenceLimits.MaxTotalBytes,
                streams,
                cancellationToken);

            return await ExecuteAsync(
                "AiImageGenerationService.GenerateFromReferences",
                (authorization, token) => Application.Ai.CreateImageFromReferences(
                    authorization,
                    idempotencyKey,
                    referenceParts,
                    request.Prompt,
                    request.AspectRatio.Value,
                    background,
                    request.Seed?.ToString(CultureInfo.InvariantCulture),
                    request.Model?.Value,
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetImageTags(activity, request));
        }
        finally
        {
            await DisposeReferenceStreamsAsync(streams);
        }
    }

    // A picture midway through being worked out. Anything that cannot be read as
    // one is passed over: it is a preview, and the finished picture is what the
    // caller is really waiting for.
    private static void ReportImagePreview(
        AiServerSentEvent item,
        IProgress<AiImagePreview> progress,
        ImagePreviewBudget budget)
    {
        if (item.Event != "partial")
            return;

        AiImagePartialDto? partial;
        try
        {
            partial = JsonSerializer.Deserialize<AiImagePartialDto>(
                item.Data,
                AiStreamJson.Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (partial is null || partial.Index < 0 || string.IsNullOrEmpty(partial.Image))
            return;

        if (budget.TryDecode(partial.Image, out byte[] bytes))
        {
            progress.Report(new AiImagePreview(partial.Index, bytes));
        }
    }

    // Counts the previews of one generation; the ones past the limit are passed over.
    internal sealed class ImagePreviewBudget
    {
        private int _count;

        public bool TryDecode(string image, out byte[] bytes)
        {
            bytes = [];
            if (_count >= MaximumImagePreviewCount)
                return false;

            try
            {
                bytes = System.Convert.FromBase64String(image);
            }
            catch (FormatException)
            {
                return false;
            }

            if (bytes.Length == 0)
                return false;

            _count++;
            return true;
        }
    }

    private sealed record AiImagePartialDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public required int Index { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("image")]
        public string? Image { get; init; }
    }

    private static void SetImageTags(Activity? activity, AiImageGenerationRequest request)
    {
        activity?.SetTag("aspectRatio", request.AspectRatio.Value);
        activity?.SetTag("background", request.Background.Value);
        activity?.SetTag("referenceCount", request.References.Count);
    }
}

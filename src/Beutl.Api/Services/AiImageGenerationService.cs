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
    private const long MaximumImagePreviewBytes = AiRequestLimits.MaxImageUploadBytes;

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
        // The caller's key when it has one: that is what lets a retry recover a
        // picture already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        // The endpoint reads "auto" and an absent background the same way, so
        // leaving it to the model is sent as nothing at all.
        string backgroundValue = request.Background.Value;
        string? background =
            backgroundValue.Length > 0 && backgroundValue != "auto" ? backgroundValue : null;

        if (request.References.Count == 0)
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

        cancellationToken.ThrowIfCancellationRequested();
        // Every reference is held open for the whole upload, so they are opened
        // together and closed together — one that failed to open must not leave
        // the ones before it behind.
        var streams = new List<Stream>(request.References.Count);
        try
        {
            var referenceParts = new List<StreamPart>(request.References.Count);
            foreach (AiUploadSource reference in request.References)
            {
                Stream stream = await AiUploadValidation.OpenAsync(
                    reference,
                    AiRequestLimits.MaxImageUploadBytes,
                    cancellationToken);
                streams.Add(stream);
                if (streams.Sum(value => value.Length - value.Position)
                    > request.ReferenceLimits.MaxTotalBytes)
                {
                    throw new AiFileTooLargeException();
                }
                referenceParts.Add(AiMultipartFormData.File(
                    stream,
                    reference.FileName,
                    reference.MediaType,
                    "reference[]"));
            }

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

    internal sealed class ImagePreviewBudget
    {
        private int _count;
        private long _bytes;

        public bool TryDecode(string image, out byte[] bytes)
        {
            bytes = [];
            int length = 0;
            int padding = 0;
            foreach (char character in image)
            {
                // Match the whitespace accepted by Convert.FromBase64String.
                if (character is ' ' or '\t' or '\r' or '\n')
                    continue;

                length++;
                padding = character == '=' ? padding + 1 : 0;
            }

            if (length == 0 || length % 4 != 0 || padding > 2)
                return false;

            long decodedLength = (long)(length / 4) * 3 - padding;
            if (!CanReserve(decodedLength))
                return false;

            try
            {
                bytes = System.Convert.FromBase64String(image);
            }
            catch (FormatException)
            {
                return false;
            }

            return TryReserve(bytes.LongLength);
        }

        private bool CanReserve(long bytes)
            => bytes > 0
               && _count < MaximumImagePreviewCount
               && bytes <= MaximumImagePreviewBytes - _bytes;

        public bool TryReserve(long bytes)
        {
            if (!CanReserve(bytes))
            {
                return false;
            }

            _count++;
            _bytes += bytes;
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

using Refit;

namespace Beutl.Api.Clients;

internal interface IAiClient
{
    [Get("/api/v3/user/entitlements")]
    Task<EntitlementsResponse> GetEntitlements(
        [Header("Authorization")] string authorization,
        CancellationToken cancellationToken);

    [Post("/api/v3/ai/images")]
    Task<AiImageResponse> CreateImage(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [Body] CreateAiImageRequest request,
        CancellationToken cancellationToken);

    [Multipart]
    [Post("/api/v3/ai/images/edit")]
    Task<AiImageResponse> EditImage(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [AliasAs("file")] StreamPart file,
        [AliasAs("task")] string task,
        [AliasAs("prompt")] string? prompt,
        CancellationToken cancellationToken);

    [Multipart]
    [Post("/api/v3/ai/transcriptions")]
    Task<AiTranscriptionResponseDto> Transcribe(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [AliasAs("file")] StreamPart file,
        [AliasAs("language")] string? language,
        CancellationToken cancellationToken);

    [Post("/api/v3/ai/videos")]
    Task<CreateAiVideoResponse> CreateVideo(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [Body] CreateAiVideoRequest request,
        CancellationToken cancellationToken);

    [Multipart]
    [Post("/api/v3/ai/videos/frames")]
    Task<CreateAiVideoResponse> CreateVideoFromFrames(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [AliasAs("firstFrame")] StreamPart firstFrame,
        [AliasAs("lastFrame")] StreamPart? lastFrame,
        [AliasAs("prompt")] string prompt,
        [AliasAs("durationSeconds")] int durationSeconds,
        [AliasAs("resolution")] string resolution,
        CancellationToken cancellationToken);

    [Get("/api/v3/ai/videos/{id}")]
    Task<AiVideoJobResponse> GetVideoJob(
        [Header("Authorization")] string authorization,
        string id,
        CancellationToken cancellationToken);

    [Get("/api/v3/ai/jobs")]
    Task<AiJobHistoryPageResponse> GetJobs(
        [Header("Authorization")] string authorization,
        [AliasAs("cursor")] string? cursor,
        [AliasAs("limit")] int limit,
        CancellationToken cancellationToken);

    [Delete("/api/v3/ai/jobs/{id}")]
    Task<DeleteAiJobResponse> DeleteJob(
        [Header("Authorization")] string authorization,
        string id,
        CancellationToken cancellationToken);

    [Post("/api/v3/ai/translations")]
    Task<AiCaptionTranslationResponseDto> Translate(
        [Header("Authorization")] string authorization,
        [Header("Idempotency-Key")] string idempotencyKey,
        [Body] AiCaptionTranslationRequestDto request,
        CancellationToken cancellationToken);

    [Post("/api/v3/user/ai-availability")]
    Task<AiOperationAvailabilityResponse> CheckFixedAvailability(
        [Header("Authorization")] string authorization,
        [Body] AiFixedOperationAvailabilityRequestDto request,
        CancellationToken cancellationToken);

    [Post("/api/v3/user/ai-availability")]
    Task<AiOperationAvailabilityResponse> CheckVideoAvailability(
        [Header("Authorization")] string authorization,
        [Body] AiVideoOperationAvailabilityRequestDto request,
        CancellationToken cancellationToken);

    [Post("/api/v3/user/ai-availability")]
    Task<AiOperationAvailabilityResponse> CheckTranscriptionAvailability(
        [Header("Authorization")] string authorization,
        [Body] AiTranscriptionOperationAvailabilityRequestDto request,
        CancellationToken cancellationToken);

    [Post("/api/v3/user/ai-availability")]
    Task<AiOperationAvailabilityResponse> CheckTranslationAvailability(
        [Header("Authorization")] string authorization,
        [Body] AiTranslationOperationAvailabilityRequestDto request,
        CancellationToken cancellationToken);
}

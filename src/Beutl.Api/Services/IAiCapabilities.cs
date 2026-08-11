using Reactive.Bindings;

namespace Beutl.Api.Services;

public interface IAiEntitlementService : IBeutlApiResource
{
    IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; }

    Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken);
}

public interface IAiImageGenerationService : IBeutlApiResource
{
    Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        CancellationToken cancellationToken);
}

public interface IAiImageEditingService : IBeutlApiResource
{
    Task<AiImageResult> EditAsync(
        AiImageEditRequest request,
        CancellationToken cancellationToken);
}

public interface IAiTranscriptionService : IBeutlApiResource
{
    Task<AiTranscriptionResponse> TranscribeAsync(
        AiTranscriptionRequest request,
        CancellationToken cancellationToken);
}

public interface IAiCaptionTranslationService : IBeutlApiResource
{
    Task<AiCaptionTranslationResponse> TranslateAsync(
        AiCaptionTranslationRequest request,
        CancellationToken cancellationToken);
}

public interface IAiVideoService : IBeutlApiResource
{
    Task<AiVideoGenerationResult> CreateAsync(
        AiVideoGenerationRequest request,
        CancellationToken cancellationToken);

    Task<AiVideoJob> GetAsync(
        AiJobId jobId,
        CancellationToken cancellationToken);
}

public interface IAuthenticatedContentService : IBeutlApiResource
{
    /// <summary>
    /// Streams authenticated content into a caller-owned writable destination. The destination
    /// remains open and ownership never transfers to the service.
    /// </summary>
    Task CopyToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken);
}

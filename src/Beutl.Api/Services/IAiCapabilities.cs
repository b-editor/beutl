using Reactive.Bindings;

namespace Beutl.Api.Services;

public interface IAiEntitlementService : IBeutlApiResource
{
    IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; }

    Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The models each operation offers. Registered on the server at runtime, so
/// unlike every other list this client holds it cannot be a set of literals and
/// has to be asked for.
/// </summary>
public interface IAiModelCatalogService : IBeutlApiResource
{
    /// <summary>
    /// The catalog, fetched once and reused. An empty catalog is what a caller
    /// gets when the server cannot be reached or offers nothing to choose from:
    /// a request then names no model and runs on the server's default, which is
    /// how this client behaved before models could be chosen at all.
    /// </summary>
    Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken);

    /// <summary>Discards the cached catalog so the next read fetches again.</summary>
    void Invalidate();
}

public interface IAiOperationAvailabilityService : IBeutlApiResource
{
    Task<bool> CheckAsync(
        AiOperationAvailabilityRequest request,
        CancellationToken cancellationToken);
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
    Task<AiContentDownload> CopyToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken);
}

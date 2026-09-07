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

    /// <summary>
    /// The same generation, reporting each rough version of the picture as the
    /// model works it out. Only models whose provider streams send any.
    /// </summary>
    Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        IProgress<AiImagePreview>? progress,
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

    /// <summary>
    /// The same translation, reported subtitle by subtitle as it arrives.
    /// </summary>
    /// <remarks>
    /// What is reported is a preview: the result returned at the end is the
    /// whole translation, checked as it always was, and a run that fails
    /// reports nothing to keep however much of it was shown.
    /// </remarks>
    Task<AiCaptionTranslationResponse> TranslateAsync(
        AiCaptionTranslationRequest request,
        IProgress<AiCaptionTranslationSegment>? progress,
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

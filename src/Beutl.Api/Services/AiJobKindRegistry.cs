using System.Diagnostics.CodeAnalysis;

namespace Beutl.Api.Services;

public interface IAiJobKindRegistry : IBeutlApiResource
{
    bool TryGetRefreshHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRefreshHandler? handler);

    bool TryGetRetryHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRetryHandler? handler);

    AiJobStatusSemantics GetStatus(AiJob job);

    AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status);
}

// The status, refresh and retry behavior of Beutl's AI job kinds. Kinds match without regard to case.
public sealed class AiJobKindRegistry : IAiJobKindRegistry
{
    private readonly Dictionary<string, IAiJobStatusResolver> _statusResolvers;
    private readonly Dictionary<string, IAiJobRefreshHandler> _refreshHandlers;
    private readonly Dictionary<string, IAiJobRetryHandler> _retryHandlers;

    internal AiJobKindRegistry(
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobStatusResolver>> statusResolvers,
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobRefreshHandler>>? refreshHandlers = null,
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobRetryHandler>>? retryHandlers = null)
    {
        _statusResolvers = ToDictionary(statusResolvers);
        _refreshHandlers = ToDictionary(refreshHandlers ?? []);
        _retryHandlers = ToDictionary(retryHandlers ?? []);
    }

    internal static AiJobKindRegistry CreateBuiltIn(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService models,
        AiRetryAttemptContext retryContext)
        => BuiltInAiJobKinds.Create(images, videos, entitlements, availability, models, retryContext);

    public bool TryGetRefreshHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRefreshHandler? handler)
        => _refreshHandlers.TryGetValue(kind.Value, out handler);

    public bool TryGetRetryHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRetryHandler? handler)
        => _retryHandlers.TryGetValue(kind.Value, out handler);

    public AiJobStatusSemantics GetStatus(AiJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return GetStatus(job.Kind, job.Status);
    }

    public AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status)
        => _statusResolvers.TryGetValue(kind.Value, out IAiJobStatusResolver? resolver)
            ? resolver.Resolve(status)
            : AiJobStatusSemantics.Unknown;

    private static Dictionary<string, T> ToDictionary<T>(IEnumerable<KeyValuePair<AiJobKindId, T>> pairs)
        => pairs.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
}

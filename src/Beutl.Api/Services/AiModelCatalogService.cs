using System.Diagnostics;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Refit;

namespace Beutl.Api.Services;

internal sealed class AiModelCatalogService : IAiModelCatalogService, IDisposable
{
    // Long enough that opening a few dialogs in a row does not fetch the list
    // again, short enough that a model the operator disables — or adds, or
    // reorders — stops being offered without the editor being restarted.
    private static readonly TimeSpan s_freshness = TimeSpan.FromMinutes(5);
    private readonly BeutlApiApplication _application;
    private readonly TimeProvider _timeProvider;
    private readonly IAiOperationCapabilitySchemaProvider _capabilitySchemas;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDisposable _authenticationSubscription;
    private readonly object _cacheGate = new();
    private AiModelCatalog? _catalog;
    private AuthenticatedUser? _catalogOwner;
    private long _catalogSchemaRevision = -1;
    private long _fetchedAt;
    private int _disposed;

    internal Action? BeforeCachePublication { get; set; }

    public AiModelCatalogService(
        BeutlApiApplication application,
        TimeProvider? timeProvider = null,
        IAiOperationCapabilitySchemaProvider? capabilitySchemas = null)
    {
        _application = application;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _capabilitySchemas = capabilitySchemas
            ?? BuiltInAiOperationCapabilitySchemaProvider.Instance;
        // The catalog is the list one account may pay for. Another account's is
        // a different list, so signing in as someone else drops it rather than
        // offering them models that were never theirs.
        _authenticationSubscription = application.AuthenticatedUser.Subscribe(_ => Invalidate());
        _capabilitySchemas.Changed += OnCapabilitySchemasChanged;
    }

    public async Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (TryGetFresh(out AiModelCatalog? cached))
            return cached!;

        using CancellationTokenSource operationCts =
            _application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (TryGetFresh(out AiModelCatalog? raced))
                return raced!;

            using Activity? activity = _application.ActivitySource.StartActivity(
                "AiModelCatalogService.Get",
                ActivityKind.Client);
            AuthenticatedUser? authenticatedUser = _application.AuthenticatedUser.Value;
            if (authenticatedUser is null)
                return AiModelCatalog.Empty;

            try
            {
                AuthenticatedApiResult<AiCapabilitiesResponse> response =
                    await _application.SendAuthenticatedAsync(
                        (authorization, requestToken) =>
                            _application.Ai.GetCapabilities(authorization, requestToken),
                        token,
                        authenticatedUser);
                AiOperationCapabilitySchemaSnapshot schemaSnapshot =
                    _capabilitySchemas.GetSnapshot();
                AiModelCatalog catalog = AiModelMapper.ToModel(response.Value, schemaSnapshot);
                BeforeCachePublication?.Invoke();
                while (true)
                {
                    AiOperationCapabilitySchemaSnapshot latestSchemaSnapshot =
                        _capabilitySchemas.GetSnapshot();
                    if (latestSchemaSnapshot.Revision != schemaSnapshot.Revision)
                    {
                        schemaSnapshot = latestSchemaSnapshot;
                        catalog = AiModelMapper.ToModel(response.Value, schemaSnapshot);
                        continue;
                    }

                    bool published = false;
                    lock (_cacheGate)
                    {
                        if (!ReferenceEquals(_application.AuthenticatedUser.Value, response.User))
                            return AiModelCatalog.Empty;

                        latestSchemaSnapshot = _capabilitySchemas.GetSnapshot();
                        if (latestSchemaSnapshot.Revision == schemaSnapshot.Revision)
                        {
                            _catalog = catalog;
                            _catalogOwner = response.User;
                            _catalogSchemaRevision = schemaSnapshot.Revision;
                            _fetchedAt = _timeProvider.GetTimestamp();
                            published = true;
                        }
                    }

                    if (!published)
                    {
                        schemaSnapshot = latestSchemaSnapshot;
                        catalog = AiModelMapper.ToModel(response.Value, schemaSnapshot);
                        continue;
                    }

                    latestSchemaSnapshot = _capabilitySchemas.GetSnapshot();
                    if (latestSchemaSnapshot.Revision == schemaSnapshot.Revision)
                        return catalog;

                    Invalidate();
                    schemaSnapshot = latestSchemaSnapshot;
                    catalog = AiModelMapper.ToModel(response.Value, schemaSnapshot);
                }
            }
            catch (ApiException ex)
            {
                // A dialog that cannot list the models still has to open. It
                // then offers no choice and the server uses its default, which
                // is exactly what this client did before it could choose.
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("statusCode", (int)ex.StatusCode);
                return AiModelCatalog.Empty;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_cacheGate)
        {
            _catalog = null;
            _catalogOwner = null;
            _catalogSchemaRevision = -1;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _authenticationSubscription.Dispose();
        _capabilitySchemas.Changed -= OnCapabilitySchemasChanged;
        // Dispose is synchronous, while an in-flight GetAsync may still own the
        // semaphore and release it after application-lifetime cancellation. The
        // gate never exposes its wait handle, so keeping it alive avoids turning
        // that expected cancellation into ObjectDisposedException.
    }

    private bool TryGetFresh(out AiModelCatalog? catalog)
    {
        while (true)
        {
            AiOperationCapabilitySchemaSnapshot schemaSnapshot =
                _capabilitySchemas.GetSnapshot();
            lock (_cacheGate)
            {
                catalog = _catalog;
                if (catalog is null
                    || !ReferenceEquals(_catalogOwner, _application.AuthenticatedUser.Value)
                    || _catalogSchemaRevision != schemaSnapshot.Revision
                    || _timeProvider.GetElapsedTime(_fetchedAt) >= s_freshness)
                {
                    return false;
                }
            }

            if (_capabilitySchemas.GetSnapshot().Revision == schemaSnapshot.Revision)
                return true;
        }
    }

    private void OnCapabilitySchemasChanged(object? sender, EventArgs e) => Invalidate();
}

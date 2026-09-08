using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

/// <summary>Owns one status-resolver registration and drains its active leases on disposal.</summary>
public interface IAiJobStatusResolverRegistration : IAsyncDisposable
{
}

/// <summary>Owns one refresh-handler registration and drains its active leases on disposal.</summary>
public interface IAiJobRefreshHandlerRegistration : IAsyncDisposable
{
}

/// <summary>Owns one retry-handler registration and drains its active leases on disposal.</summary>
public interface IAiJobRetryHandlerRegistration : IAsyncDisposable
{
}

/// <summary>Keeps one status resolver alive for an operation.</summary>
public interface IAiJobStatusResolverLease : IDisposable
{
    IAiJobStatusResolver Resolver { get; }
}

/// <summary>Keeps one refresh handler alive through its asynchronous refresh.</summary>
public interface IAiJobRefreshHandlerLease : IDisposable
{
    IAiJobRefreshHandler Handler { get; }
}

/// <summary>
/// Keeps one retry handler alive through preflight, confirmation, preparation execution, and
/// preparation disposal.
/// </summary>
public interface IAiJobRetryHandlerLease : IDisposable
{
    IAiJobRetryHandler Handler { get; }
}

public interface IAiJobKindRegistry : IBeutlApiResource
{
    IAiJobStatusResolverRegistration Register(AiJobStatusResolverRegistration registration);

    IAiJobRefreshHandlerRegistration Register(AiJobRefreshHandlerRegistration registration);

    IAiJobRetryHandlerRegistration Register(AiJobRetryHandlerRegistration registration);

    bool TryAcquireStatusResolver(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobStatusResolverLease? lease);

    bool TryAcquireRefreshHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRefreshHandlerLease? lease);

    bool TryAcquireRetryHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRetryHandlerLease? lease);

    AiJobStatusSemantics GetStatus(AiJob job);

    AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status);
}

/// <summary>
/// Resolves status, refresh, and retry behavior through independent Add/Replace slots.
/// </summary>
public sealed class AiJobKindRegistry : IAiJobKindRegistry, IAsyncDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiJobKindRegistry>();
    private readonly AiJobSlotRegistry<
        IAiJobStatusResolver,
        AiJobStatusResolverRegistration,
        AiJobStatusResolverExtension> _statusResolvers;
    private readonly AiJobSlotRegistry<
        IAiJobRefreshHandler,
        AiJobRefreshHandlerRegistration,
        AiJobRefreshHandlerExtension> _refreshHandlers;
    private readonly AiJobSlotRegistry<
        IAiJobRetryHandler,
        AiJobRetryHandlerRegistration,
        AiJobRetryHandlerExtension> _retryHandlers;

    public AiJobKindRegistry()
        : this([], [], [], null)
    {
    }

    public AiJobKindRegistry(
        IEnumerable<AiJobStatusResolverRegistration> statusResolvers,
        IEnumerable<AiJobRefreshHandlerRegistration> refreshHandlers,
        IEnumerable<AiJobRetryHandlerRegistration> retryHandlers)
        : this(statusResolvers, refreshHandlers, retryHandlers, null)
    {
    }

    internal AiJobKindRegistry(
        IEnumerable<AiJobStatusResolverRegistration> statusResolvers,
        IEnumerable<AiJobRefreshHandlerRegistration> refreshHandlers,
        IEnumerable<AiJobRetryHandlerRegistration> retryHandlers,
        IExtensionRegistry? extensionProvider)
    {
        _statusResolvers = new AiJobSlotRegistry<
            IAiJobStatusResolver,
            AiJobStatusResolverRegistration,
            AiJobStatusResolverExtension>(
                statusResolvers,
                static registration => registration.Kind,
                static registration => registration.Resolver,
                static registration => ToSlotMode(registration.Mode),
                static (kind, capability) => new AiJobStatusResolverRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                static (extension, exception) => ReportFailure(
                    extension,
                    "status resolver",
                    exception));
        _refreshHandlers = new AiJobSlotRegistry<
            IAiJobRefreshHandler,
            AiJobRefreshHandlerRegistration,
            AiJobRefreshHandlerExtension>(
                refreshHandlers,
                static registration => registration.Kind,
                static registration => registration.Handler,
                static registration => ToSlotMode(registration.Mode),
                static (kind, capability) => new AiJobRefreshHandlerRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                static (extension, exception) => ReportFailure(
                    extension,
                    "refresh handler",
                    exception));
        _retryHandlers = new AiJobSlotRegistry<
            IAiJobRetryHandler,
            AiJobRetryHandlerRegistration,
            AiJobRetryHandlerExtension>(
                retryHandlers,
                static registration => registration.Kind,
                static registration => registration.Handler,
                static registration => ToSlotMode(registration.Mode),
                static (kind, capability) => new AiJobRetryHandlerRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                static (extension, exception) => ReportFailure(
                    extension,
                    "retry handler",
                    exception));
    }

    internal static AiJobKindRegistry CreateBuiltIn(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService models,
        AiRetryAttemptContext retryContext,
        IExtensionRegistry? extensionProvider = null)
    {
        BuiltInAiJobBehaviorRegistrations registrations = BuiltInAiJobKinds.Create(
            images,
            videos,
            entitlements,
            availability,
            models,
            retryContext);
        return new AiJobKindRegistry(
            registrations.StatusResolvers,
            registrations.RefreshHandlers,
            registrations.RetryHandlers,
            extensionProvider);
    }

    public IAiJobStatusResolverRegistration Register(AiJobStatusResolverRegistration registration)
        => new StatusResolverRegistration(_statusResolvers.Register(registration));

    public IAiJobRefreshHandlerRegistration Register(AiJobRefreshHandlerRegistration registration)
        => new RefreshHandlerRegistration(_refreshHandlers.Register(registration));

    public IAiJobRetryHandlerRegistration Register(AiJobRetryHandlerRegistration registration)
        => new RetryHandlerRegistration(_retryHandlers.Register(registration));

    public bool TryAcquireStatusResolver(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobStatusResolverLease? lease)
    {
        if (_statusResolvers.TryAcquire(kind, out AiJobSlotLease<IAiJobStatusResolver>? inner))
        {
            lease = new StatusResolverLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public bool TryAcquireRefreshHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRefreshHandlerLease? lease)
    {
        if (_refreshHandlers.TryAcquire(kind, out AiJobSlotLease<IAiJobRefreshHandler>? inner))
        {
            lease = new RefreshHandlerLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public bool TryAcquireRetryHandler(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobRetryHandlerLease? lease)
    {
        if (_retryHandlers.TryAcquire(kind, out AiJobSlotLease<IAiJobRetryHandler>? inner))
        {
            lease = new RetryHandlerLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public AiJobStatusSemantics GetStatus(AiJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return GetStatus(job.Kind, job.Status);
    }

    public AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status)
    {
        if (!TryAcquireStatusResolver(kind, out IAiJobStatusResolverLease? lease))
            return AiJobStatusSemantics.Unknown;

        using (lease)
        {
            return lease.Resolver.Resolve(status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            _statusResolvers.DisposeAsync().AsTask(),
            _refreshHandlers.DisposeAsync().AsTask(),
            _retryHandlers.DisposeAsync().AsTask());
    }

    private static AiJobSlotRegistrationMode ToSlotMode(AiJobBehaviorRegistrationMode mode)
        => mode == AiJobBehaviorRegistrationMode.Replace
            ? AiJobSlotRegistrationMode.Replace
            : AiJobSlotRegistrationMode.Add;

    private static void ReportFailure(
        Extension extension,
        string capability,
        Exception exception)
        => s_logger.LogWarning(
            exception,
            "Ignoring invalid AI job {Capability} contribution from {ExtensionType}.",
            capability,
            extension.GetType().FullName ?? extension.GetType().Name);

    private sealed class StatusResolverRegistration(
        IAiJobSlotRegistration inner) : IAiJobStatusResolverRegistration
    {
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RefreshHandlerRegistration(
        IAiJobSlotRegistration inner) : IAiJobRefreshHandlerRegistration
    {
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RetryHandlerRegistration(
        IAiJobSlotRegistration inner) : IAiJobRetryHandlerRegistration
    {
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class StatusResolverLease(
        AiJobSlotLease<IAiJobStatusResolver> inner) : IAiJobStatusResolverLease
    {
        public IAiJobStatusResolver Resolver => inner.Capability;

        public void Dispose() => inner.Dispose();
    }

    private sealed class RefreshHandlerLease(
        AiJobSlotLease<IAiJobRefreshHandler> inner) : IAiJobRefreshHandlerLease
    {
        public IAiJobRefreshHandler Handler => inner.Capability;

        public void Dispose() => inner.Dispose();
    }

    private sealed class RetryHandlerLease(
        AiJobSlotLease<IAiJobRetryHandler> inner) : IAiJobRetryHandlerLease
    {
        public IAiJobRetryHandler Handler => inner.Capability;

        public void Dispose() => inner.Dispose();
    }
}

using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Models;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Opaque, handler-owned state produced during preflight and consumed during materialization.
/// Returning it through <see cref="ElementSourcePreflightResult.Ready"/> transfers lease ownership
/// to the host, which asynchronously disposes it exactly once after the whole batch finishes,
/// including validation, cancellation, persistence, and rollback failures.
/// </summary>
public interface IElementSourcePreflight : IAsyncDisposable
{
}

public sealed record ElementSourcePreflightContext(
    Scene Scene,
    ElementDescription Description);

public sealed class ElementSourcePreflightResult
{
    private ElementSourcePreflightResult(
        IElementSourcePreflight? preflight,
        IReadOnlyList<int> targetLayers,
        ElementAddFailure? failure)
    {
        Preflight = preflight;
        TargetLayers = targetLayers;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public IElementSourcePreflight? Preflight { get; }

    public IReadOnlyList<int> TargetLayers { get; }

    public ElementAddFailure? Failure { get; }

    public static ElementSourcePreflightResult Ready(
        IElementSourcePreflight preflight,
        IEnumerable<int> targetLayers)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(targetLayers);
        int[] layers = targetLayers.Distinct().ToArray();
        if (layers.Length == 0)
            throw new ArgumentException("Preflight must reserve at least one target layer.", nameof(targetLayers));

        return new ElementSourcePreflightResult(preflight, Array.AsReadOnly(layers), null);
    }

    public static ElementSourcePreflightResult Rejected(ElementAddFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ElementSourcePreflightResult(null, Array.Empty<int>(), failure);
    }
}

public sealed record ElementSourceMaterializationContext(
    Scene Scene,
    ElementDescription Description);

public sealed class ElementMaterializationResource : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private ElementMaterializationResource(Func<ValueTask> disposeAsync)
    {
        _disposeAsync = disposeAsync;
    }

    public static ElementMaterializationResource Temporary(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new ElementMaterializationResource(
            () =>
            {
                resource.Dispose();
                return ValueTask.CompletedTask;
            });
    }

    public static ElementMaterializationResource TemporaryAsync(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new ElementMaterializationResource(
            resource.DisposeAsync);
    }

    public ValueTask DisposeAsync()
        => Interlocked.Exchange(ref _disposeAsync, null)?.Invoke() ?? ValueTask.CompletedTask;
}

public sealed class ElementMaterialization
{
    public ElementMaterialization(
        Element primaryElement,
        IEnumerable<Element>? companionElements = null,
        IEnumerable<IReadOnlySet<Guid>>? groups = null,
        IEnumerable<ElementMaterializationResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(primaryElement);
        Element[] companions = companionElements?.ToArray() ?? [];
        IReadOnlySet<Guid>[] groupArray = groups?.ToArray() ?? [];
        ElementMaterializationResource[] resourceArray = resources?.ToArray() ?? [];
        if (companions.Any(element => element is null))
            throw new ArgumentException("Companion elements cannot contain null.", nameof(companionElements));
        if (groupArray.Any(group => group is null))
            throw new ArgumentException("Groups cannot contain null.", nameof(groups));
        if (resourceArray.Any(resource => resource is null))
            throw new ArgumentException("Resources cannot contain null.", nameof(resources));

        PrimaryElement = primaryElement;
        CompanionElements = Array.AsReadOnly(companions);
        Elements = Array.AsReadOnly(new[] { primaryElement }.Concat(companions).ToArray());
        Groups = Array.AsReadOnly(groupArray);
        Resources = Array.AsReadOnly(resourceArray);
    }

    public Element PrimaryElement { get; }

    public IReadOnlyList<Element> CompanionElements { get; }

    public IReadOnlyList<Element> Elements { get; }

    public IReadOnlyList<IReadOnlySet<Guid>> Groups { get; }

    /// <summary>
    /// Resource leases acquired during materialization. The host releases every lease after the
    /// materialization pipeline finishes. A handler that needs a resource to outlive materialization
    /// must attach it to the returned element graph itself.
    /// </summary>
    public IReadOnlyList<ElementMaterializationResource> Resources { get; }
}

public sealed class ElementSourceMaterializationResult
{
    private ElementSourceMaterializationResult(
        ElementMaterialization? materialization,
        ElementAddFailure? failure)
    {
        Materialization = materialization;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public ElementMaterialization? Materialization { get; }

    public ElementAddFailure? Failure { get; }

    public static ElementSourceMaterializationResult Materialized(ElementMaterialization materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        return new ElementSourceMaterializationResult(materialization, null);
    }

    public static ElementSourceMaterializationResult Rejected(ElementAddFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ElementSourceMaterializationResult(null, failure);
    }
}

/// <summary>
/// Performs source-specific preflight and materialization. Implementations are registered by
/// their exact <see cref="SourceType"/> so third-party element sources do not require host changes.
/// </summary>
public interface IElementSourceHandler
{
    Type SourceType { get; }

    ValueTask<ElementSourcePreflightResult> PreflightAsync(
        ElementSourcePreflightContext context,
        CancellationToken cancellationToken);

    ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
        ElementSourceMaterializationContext context,
        IElementSourcePreflight preflight,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contributes element source handlers from an extension package. Registrations are dynamically
/// composed for every open editor and retired before the package unloads.
/// </summary>
public abstract class ElementSourceHandlerExtension : Extension
{
    public abstract IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; }
}

public sealed record ElementSourceHandlerExtensionFailure(
    string ExtensionType,
    Exception Exception);

/// <summary>
/// Owns a source-handler registration. Retain this object while the handler is available and
/// dispose it before unloading handler-owned resources. Disposal retires the handler, prevents
/// new operation leases, and waits for existing operation leases to finish.
/// </summary>
public interface IElementSourceHandlerRegistration : IDisposable
{
}

/// <summary>
/// Keeps a resolved handler alive for one operation. Consumers must not cache
/// <see cref="Handler"/> beyond this lease.
/// </summary>
public interface IElementSourceHandlerLease : IDisposable
{
    IElementSourceHandler Handler { get; }
}

public interface IElementSourceHandlerRegistry
{
    /// <summary>
    /// Gets the active handlers for inspection. Acquire an <see cref="IElementSourceHandlerLease"/>
    /// before invoking a handler.
    /// </summary>
    IReadOnlyList<IElementSourceHandler> Handlers { get; }

    IElementSourceHandlerRegistration Register(ElementSourceHandlerRegistration registration);

    bool TryAcquire(
        Type sourceType,
        [NotNullWhen(true)] out IElementSourceHandlerLease? lease);
}

public enum ElementSourceHandlerRegistrationMode
{
    Add,
    Replace,
}

/// <summary>
/// Registers a handler with explicit collision and deterministic enumeration semantics.
/// </summary>
public sealed class ElementSourceHandlerRegistration
{
    public ElementSourceHandlerRegistration(
        IElementSourceHandler handler,
        ElementSourceHandlerRegistrationMode mode = ElementSourceHandlerRegistrationMode.Add,
        int order = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Handler = handler;
        Mode = mode;
        Order = order;
    }

    public IElementSourceHandler Handler { get; }

    public ElementSourceHandlerRegistrationMode Mode { get; }

    public int Order { get; }
}

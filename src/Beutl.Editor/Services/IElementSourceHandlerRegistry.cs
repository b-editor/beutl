using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

public sealed record ElementSourceHandlerExtensionFailure(
    string ExtensionType,
    Exception Exception);

/// <summary>
/// Owns a source-handler registration. Retain this object while the handler is available and
/// asynchronously dispose it before unloading handler-owned resources. Disposal first retires
/// the handler synchronously, then asynchronously waits for existing operation leases to finish.
/// </summary>
public interface IElementSourceHandlerRegistration : IAsyncDisposable
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
    bool TryAcquire(
        Type sourceType,
        [NotNullWhen(true)] out IElementSourceHandlerLease? lease);
}

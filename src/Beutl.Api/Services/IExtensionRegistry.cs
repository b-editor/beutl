using Beutl.Extensibility;

namespace Beutl.Api.Services;

/// <summary>
/// Owns the mutable set of extensions registered for the current application session.
/// </summary>
public interface IExtensionRegistry : IExtensionProvider, IBeutlApiResource
{
    /// <summary>
    /// Registers all extensions owned by one package.
    /// </summary>
    void AddExtensions(int packageId, IReadOnlyList<Extension> extensions);

    /// <summary>
    /// Gets a stable snapshot of the extensions owned by one package. The result must contain the
    /// same extension instances that a subsequent removal ticket exposes.
    /// </summary>
    IReadOnlyList<Extension> GetPackageExtensions(int packageId);

    /// <summary>
    /// Removes all extensions owned by one package from every public registry view and returns the
    /// drain ticket that must complete before their code can unload. Implementations must throw
    /// <see cref="ExtensionRemovalNotificationException"/> if an observer fails after removal.
    /// </summary>
    ExtensionRemoval RemoveExtensions(int packageId);

    /// <summary>
    /// Runs a synchronous registry transition without racing package addition or removal.
    /// </summary>
    /// <remarks>
    /// The action may inspect or mutate this registry. Implementations must allow reentrant calls
    /// from the same thread because a coordinated transition can call <see cref="RemoveExtensions"/>.
    /// </remarks>
    void SynchronizeMutation(Action action);
}

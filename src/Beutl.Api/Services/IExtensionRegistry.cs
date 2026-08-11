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
    /// Removes and returns all extensions owned by one package.
    /// </summary>
    IReadOnlyList<Extension> RemoveExtensions(int packageId);
}

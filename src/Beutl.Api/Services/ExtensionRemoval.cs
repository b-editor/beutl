using Beutl.Extensibility;

namespace Beutl.Api.Services;

/// <summary>
/// Owns one atomic extension-removal batch and the registrations that must drain before unload.
/// </summary>
public sealed class ExtensionRemoval
{
    private readonly ExtensionRemovalDrain _drain;

    /// <summary>
    /// Creates a removal ticket after the extensions have been synchronously retired from the
    /// registry's public view.
    /// </summary>
    public ExtensionRemoval(IReadOnlyList<Extension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        Extension[] snapshot = extensions.ToArray();
        _drain = ExtensionRegistrationLifetimes.SealRemoval(snapshot);
        Extensions = snapshot;
    }

    public IReadOnlyList<Extension> Extensions { get; }

    public ValueTask DrainAsync() => new(_drain.DrainAllAsync());
}

/// <summary>
/// Reports that registry observers failed after an extension batch was synchronously removed.
/// The attached ticket must still be drained before the package can be unloaded.
/// </summary>
public sealed class ExtensionRemovalNotificationException(
    ExtensionRemoval removal,
    Exception innerException)
    : Exception("An extension removal observer failed.", innerException)
{
    public ExtensionRemoval Removal { get; } = removal
        ?? throw new ArgumentNullException(nameof(removal));
}

/// <summary>
/// Reports that registry observers rejected an extension batch after it was published and then
/// synchronously retired. The attached ticket must drain before the package code can unload.
/// </summary>
public sealed class ExtensionRegistrationNotificationException(
    ExtensionRemoval removal,
    Exception innerException)
    : Exception("An extension registration observer failed.", innerException)
{
    public ExtensionRemoval Removal { get; } = removal
        ?? throw new ArgumentNullException(nameof(removal));
}

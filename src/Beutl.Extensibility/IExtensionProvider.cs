namespace Beutl.Extensibility;

public readonly record struct ExtensionId(Guid Value)
{
    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// Describes a registered extension without retaining its package-owned instance or type.
/// </summary>
public sealed class ExtensionDescriptor
{
    public ExtensionDescriptor(ExtensionId id, string typeName)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentException("An extension identifier is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        Id = id;
        TypeName = typeName.Trim();
    }

    public ExtensionId Id { get; }

    public string TypeName { get; }
}

/// <summary>
/// Keeps one package-owned extension alive for an operation. Dispose the lease when no further
/// extension code can execute or be retained by the operation.
/// </summary>
public interface IExtensionLease<out TExtension> : IDisposable
    where TExtension : Extension
{
    TExtension Extension { get; }
}

/// <summary>
/// A read-only, lease-backed view over the registered extensions. Enumeration returns copied
/// metadata only; executable package instances are available solely through explicit leases.
/// </summary>
public interface IExtensionProvider
{
    /// <summary>
    /// Occurs after the registered extension metadata changes. Handlers should re-read
    /// <see cref="Extensions"/> or <see cref="GetDescriptors{TExtension}"/>, return promptly,
    /// and unsubscribe before their package is unloaded.
    /// </summary>
    event EventHandler? ExtensionsChanged;

    /// <summary>Gets metadata for every registered extension.</summary>
    IReadOnlyList<ExtensionDescriptor> Extensions { get; }

    /// <summary>Gets metadata for extensions assignable to <typeparamref name="TExtension"/>.</summary>
    IReadOnlyList<ExtensionDescriptor> GetDescriptors<TExtension>()
        where TExtension : Extension;

    /// <summary>Acquires the extension identified by <paramref name="id"/> for one operation.</summary>
    bool TryAcquire<TExtension>(
        ExtensionId id,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IExtensionLease<TExtension>? lease)
        where TExtension : Extension;
}

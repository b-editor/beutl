using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensibility;

/// <summary>
/// Host services supplied to <see cref="EditorExtension.TryCreateContext"/> so a created
/// <see cref="IEditorContext"/> can reach host capabilities. The host owns the instance and
/// passes it in explicitly; an extension that needs nothing from the host may ignore it.
/// </summary>
public interface IEditorContextServices
{
    /// <summary>Gets the host's extension provider, for querying other registered extensions.</summary>
    IExtensionProvider ExtensionProvider { get; }

    /// <summary>
    /// Resolves a host-provided service of type <typeparamref name="T"/> by type. This is the
    /// abstraction's escape hatch for capabilities that live downstream of
    /// <c>Beutl.Extensibility</c> (for example the host's editor service), so an extension can reach
    /// them without downcasting to the host's concrete implementation of
    /// <see cref="IEditorContextServices"/>. Downcasting would defeat the abstraction — a hand-written fake could never
    /// satisfy it — so implementers must honor this by-type lookup instead.
    /// </summary>
    /// <typeparam name="T">The reference type of the requested service.</typeparam>
    /// <param name="service">The resolved service when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a service of type <typeparamref name="T"/> was found.</returns>
    bool TryGetService<T>([NotNullWhen(true)] out T? service)
        where T : class;
}

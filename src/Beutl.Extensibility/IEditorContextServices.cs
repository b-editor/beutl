using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensibility;

/// <summary>
/// Host services supplied to <see cref="EditorExtension.TryCreateContext"/> so a created
/// <see cref="IEditorContext"/> can reach host capabilities. The host owns the instance and
/// passes it in explicitly. Every successful context creation must retain
/// <see cref="CloseService"/> and expose it, directly or through a context-specific wrapper,
/// through <see cref="IEditorContext.CloseService"/>.
/// </summary>
public interface IEditorContextServices
{
    /// <summary>Gets the host's extension provider, for querying other registered extensions.</summary>
    IExtensionProvider ExtensionProvider { get; }

    /// <summary>Gets the required host close capability to retain on the created context.</summary>
    IEditorContextCloseService CloseService { get; }

    /// <summary>
    /// Resolves a host-provided service of type <typeparamref name="T"/> by type. This is the
    /// escape hatch for capabilities that live downstream of <c>Beutl.Extensibility</c> (for example
    /// the host's editor service), letting an extension reach them without downcasting to the host's
    /// concrete implementation of <see cref="IEditorContextServices"/>. Implementers must honor this
    /// by-type lookup rather than assume the concrete host type.
    /// </summary>
    /// <typeparam name="T">The reference type of the requested service.</typeparam>
    /// <param name="service">The resolved service when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a service of type <typeparamref name="T"/> was found.</returns>
    bool TryGetService<T>([NotNullWhen(true)] out T? service)
        where T : class;
}

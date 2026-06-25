using Beutl.Collections;

namespace Beutl.Extensibility;

/// <summary>
/// A read-only view over the registered extensions. Exposed to extension authors (for example
/// through <see cref="IEditorContextServices"/>) so they can query other extensions without
/// referencing the host's concrete extension provider implementation.
/// </summary>
public interface IExtensionProvider
{
    /// <summary>Gets every registered extension.</summary>
    ICoreReadOnlyList<Extension> AllExtensions { get; }

    /// <summary>Gets all registered extensions assignable to <typeparamref name="TExtension"/>.</summary>
    TExtension[] GetExtensions<TExtension>()
        where TExtension : Extension;

    /// <summary>Finds the editor extension that supports <paramref name="file"/>, or <see langword="null"/>.</summary>
    EditorExtension? MatchEditorExtension(string file);
}

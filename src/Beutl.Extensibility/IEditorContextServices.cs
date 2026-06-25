namespace Beutl.Extensibility;

/// <summary>
/// Host services supplied to <see cref="EditorExtension.TryCreateContext"/> so a created
/// <see cref="IEditorContext"/> can reach host capabilities without relying on global singletons.
/// The host owns the instance and passes it in explicitly; an extension that needs nothing from
/// the host may ignore it.
/// </summary>
public interface IEditorContextServices
{
    /// <summary>Gets the host's extension provider, for querying other registered extensions.</summary>
    IExtensionProvider ExtensionProvider { get; }
}

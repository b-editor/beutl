using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Services;

// Host services handed to EditorExtension.TryCreateContext. The host-internal EditorService is
// downstream of Beutl.Extensibility and so cannot be a typed interface member; extensions reach it
// through the IEditorContextServices.TryGetService<T> lookup rather than downcasting to this type.
internal sealed class EditorContextServices(EditorService editorService, ExtensionProvider extensionProvider)
    : IEditorContextServices
{
    IExtensionProvider IEditorContextServices.ExtensionProvider => extensionProvider;

    public bool TryGetService<T>([NotNullWhen(true)] out T? service)
        where T : class
    {
        // Resolve by assignability so both the concrete ExtensionProvider and its IExtensionProvider
        // interface (and the host-internal EditorService) are reachable by type.
        service = editorService as T ?? extensionProvider as T;
        return service is not null;
    }
}

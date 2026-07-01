using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Services;

// Host services handed to EditorExtension.TryCreateContext so a created editor context can
// receive the services it needs. Extensions resolve host capabilities through the abstract
// IEditorContextServices.TryGetService<T> lookup (including the host-internal EditorService,
// which is downstream of Beutl.Extensibility and so cannot be a typed interface member) rather
// than downcasting to this concrete type — that keeps the abstraction testable with a fake.
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

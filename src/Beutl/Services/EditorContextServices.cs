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

    IEditorContextCloseService IEditorContextServices.CloseService => editorService;

    public bool TryGetService<T>([NotNullWhen(true)] out T? service)
        where T : class
    {
        // The concrete provider exposes host-only package instances. Extensions receive only the
        // lease-backed public interface, so TryGetService cannot be used to downcast around it.
        service = editorService as T;
        if (service is null && typeof(T) == typeof(IExtensionProvider))
            service = (T)(object)(IExtensionProvider)extensionProvider;
        return service is not null;
    }
}

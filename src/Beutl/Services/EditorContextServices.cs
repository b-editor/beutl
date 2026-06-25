using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Services;

// Host services handed to EditorExtension.TryCreateContext so a created editor context can
// receive the services it needs. Plugins consume the typed IEditorContextServices surface;
// in-tree extensions (e.g. SceneEditorExtension) cast to this concrete type to also obtain
// the host-internal EditorService.
internal sealed class EditorContextServices(EditorService editorService, ExtensionProvider extensionProvider)
    : IEditorContextServices
{
    public ExtensionProvider ExtensionProvider => extensionProvider;

    public EditorService EditorService => editorService;

    IExtensionProvider IEditorContextServices.ExtensionProvider => extensionProvider;
}

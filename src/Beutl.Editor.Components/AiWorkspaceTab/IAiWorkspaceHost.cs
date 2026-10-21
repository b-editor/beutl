using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

namespace Beutl.Editor.Components.AiWorkspaceTab;

// Builds the AI tab over the clients the app signs in with.
internal interface IAiWorkspaceHost
{
    AiWorkspaceViewModel? CreateWorkspace(IEditorContext editorContext);
}

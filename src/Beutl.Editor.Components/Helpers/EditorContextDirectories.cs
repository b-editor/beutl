using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.Helpers;

internal static class EditorContextDirectories
{
    // The saved project's folder, else the saved scene's; null while neither has been saved.
    public static string? GetProjectOrSceneDirectory(IEditorContext editorContext)
    {
        Scene? scene = editorContext.GetService<Scene>();
        Project? project = scene?.FindHierarchicalParent<Project>();
        if (project?.Uri is { } projectUri
            && Path.GetDirectoryName(projectUri.LocalPath) is { Length: > 0 } projectDirectory)
        {
            return projectDirectory;
        }

        if (scene?.Uri is { } sceneUri
            && Path.GetDirectoryName(sceneUri.LocalPath) is { Length: > 0 } sceneDirectory)
        {
            return sceneDirectory;
        }

        return null;
    }
}
